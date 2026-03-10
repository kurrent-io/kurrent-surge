#!/usr/bin/env dotnet-run
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*

using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<ChangelogCommand>();
app.Configure(c => c.SetApplicationName("changelog"));
return app.Run(args);

// ─── Models ───────────────────────────────────────────────────────────────────

record ConventionalCommit(
    string  Hash,
    string  ShortHash,
    string  Type,
    string? Scope,
    string  Subject,
    bool    IsBreaking,
    string? BreakingNote,
    int?    PrNumber  = null,
    string? PrAuthor  = null
);

// ─── Settings ─────────────────────────────────────────────────────────────────

sealed class ChangelogSettings : CommandSettings {
    [CommandOption("--from")]
    [Description("Tag to start from — defaults to the tag before --to")]
    public string? From { get; init; }

    [CommandOption("--to")]
    [Description("Tag to end at — defaults to the latest tag")]
    public string? To { get; init; }

    [CommandOption("--version")]
    [Description("Version label for the section header — inferred from --to if omitted")]
    public string? Version { get; init; }

    [CommandOption("--repo-url")]
    [Description("Repository URL for compare links — auto-detected from git remote")]
    public string? RepoUrl { get; init; }

    [CommandOption("--output")]
    [Description("Path to CHANGELOG.md (default: ./CHANGELOG.md)")]
    public string Output { get; init; } = "CHANGELOG.md";

    [CommandOption("--tag-prefix")]
    [Description("Tag prefix (default: v)")]
    public string TagPrefix { get; init; } = "v";

    [CommandOption("--section-file")]
    [Description("Write the generated section to this file (e.g. /tmp/release-notes.md) — for use as GitHub Release body")]
    public string? SectionFile { get; init; }

    [CommandOption("--dry-run")]
    [Description("Print the generated section without modifying CHANGELOG.md")]
    public bool DryRun { get; init; }
}

// ─── Command ──────────────────────────────────────────────────────────────────

sealed class ChangelogCommand : Command<ChangelogSettings> {
    static readonly Regex CommitPattern = new(
        @"^(?<type>feat|fix|docs|chore|refactor|perf|test|build|ci|revert)(?:\((?<scope>[^)]+)\))?(?<breaking>!)?: (?<subject>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex BreakingFooterPattern = new(
        @"^BREAKING[- ]CHANGE: (?<note>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public override int Execute(CommandContext context, ChangelogSettings settings, CancellationToken cancellation = default) {
        AnsiConsole.Write(new FigletText("changelog").Color(Color.MediumPurple));
        AnsiConsole.MarkupLine("[grey]Keep a Changelog 1.1.0 — from conventional commits[/]\n");

        // ── Resolve tags ──────────────────────────────────────────────────────
        var toTag = settings.To ?? Git.GetLastTag(settings.TagPrefix);
        if (toTag is null) {
            AnsiConsole.MarkupLine("[red]❌ No tags found. Create a tag first or pass --to[/]");
            return 1;
        }

        var fromTag = settings.From ?? Git.GetPreviousTag(toTag, settings.TagPrefix);
        var version = settings.Version ?? toTag.TrimStart(settings.TagPrefix.ToCharArray());
        var repoUrl = settings.RepoUrl ?? Git.GetRepoUrl();

        PrintConfig(toTag, fromTag, version, settings.Output, settings.SectionFile, repoUrl, settings.DryRun);

        // ── Parse commits ─────────────────────────────────────────────────────
        var raw     = Git.GetCommitLog(fromTag, toTag);
        var commits = raw
            .Select(c => ParseCommit(c.Hash, c.ShortHash, c.Subject, c.Body))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        AnsiConsole.MarkupLine(
            $"[grey]Commits in range:[/] {raw.Count} total, " +
            $"[white]{commits.Count}[/] conventional\n");

        if (commits.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]⚠  No conventional commits in range — nothing to add to changelog[/]");
            return 0;
        }

        // ── Enrich with PR info ───────────────────────────────────────────────
        var repoPath = ExtractRepoPath(repoUrl);
        if (repoPath is not null) {
            AnsiConsole.MarkupLine("[grey]Looking up PR attribution...[/]");
            commits = EnrichWithPrInfo(commits, repoPath);
        }

        // ── Generate section ──────────────────────────────────────────────────
        var section = GenerateSection(version, fromTag, toTag, commits, repoUrl);

        AnsiConsole.Write(new Rule("[mediumPurple]Generated Section[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(section);
        AnsiConsole.WriteLine();

        if (settings.SectionFile is not null) {
            File.WriteAllText(settings.SectionFile, section);
            AnsiConsole.MarkupLine($"[grey]Section written to[/] [white]{settings.SectionFile}[/]\n");
        }

        if (settings.DryRun) {
            AnsiConsole.MarkupLine("[yellow]Dry run — CHANGELOG.md not modified[/]");
            return 0;
        }

        // ── Update CHANGELOG.md ───────────────────────────────────────────────
        UpdateChangelogFile(settings.Output, version, fromTag, toTag, section, repoUrl);

        AnsiConsole.MarkupLine($"[green]✅ Updated[/] [white]{settings.Output}[/]");
        return 0;
    }

    // ─── Commit parsing ───────────────────────────────────────────────────────

    ConventionalCommit? ParseCommit(string hash, string shortHash, string subject, string body) {
        var match = CommitPattern.Match(subject);
        if (!match.Success) return null;

        var type     = match.Groups["type"].Value.ToLowerInvariant();
        var scope    = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
        var subj     = match.Groups["subject"].Value;
        var hasBang  = match.Groups["breaking"].Success;
        var footer   = BreakingFooterPattern.Match(body);
        var breaking = hasBang || footer.Success;

        return new(hash, shortHash, type, scope, subj, breaking,
            footer.Success ? footer.Groups["note"].Value : null);
    }

    // ─── Section generation ───────────────────────────────────────────────────
    //
    // Conventional commit → Keep a Changelog section:
    //   feat              → Added
    //   fix               → Fixed          (unless scoped security/auth → Security)
    //   perf, refactor    → Changed
    //   revert            → Removed
    //   breaking (any)    → ⚠ Breaking Changes  (above all other sections)
    //   docs, chore, test, build, ci → omitted

    static string GenerateSection(
        string version,
        string? fromTag,
        string toTag,
        List<ConventionalCommit> commits,
        string? repoUrl)
    {
        var sb   = new StringBuilder();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        sb.AppendLine($"## [{version}] - {date}");
        sb.AppendLine();

        var breaking = commits.Where(c => c.IsBreaking).ToList();
        var added    = commits.Where(c => c.Type == "feat"               && !c.IsBreaking).ToList();
        var changed  = commits.Where(c => c.Type is "perf" or "refactor" && !c.IsBreaking).ToList();
        var removed  = commits.Where(c => c.Type == "revert"             && !c.IsBreaking).ToList();
        var allFixed = commits.Where(c => c.Type == "fix"                && !c.IsBreaking).ToList();
        var security = allFixed.Where(c => c.Scope is "security" or "auth").ToList();
        var fixed_   = allFixed.Except(security).ToList();

        void Section(string title, IEnumerable<ConventionalCommit> items, Func<ConventionalCommit, string> text) {
            var list = items.ToList();
            if (list.Count == 0) return;
            sb.AppendLine($"### {title}");
            sb.AppendLine();
            foreach (var c in list)
                sb.AppendLine(FormatEntry(c, text(c)));
            sb.AppendLine();
        }

        Section("⚠ Breaking Changes", breaking, c => c.BreakingNote ?? c.Subject);
        Section("Added",    added,    c => c.Subject);
        Section("Changed",  changed,  c => c.Subject);
        Section("Removed",  removed,  c => c.Subject);
        Section("Fixed",    fixed_,   c => c.Subject);
        Section("Security", security, c => c.Subject);

        return sb.ToString().TrimEnd();
    }

    // PR attribution appended when available: — @author in #123
    // No PR found (direct push, or gh lookup failed) → entry renders without it.
    static string FormatEntry(ConventionalCommit c, string subject) {
        var scope       = c.Scope is not null ? $"**{c.Scope}:** " : "";
        var attribution = c.PrNumber is not null && c.PrAuthor is not null
            ? $" — @{c.PrAuthor} in #{c.PrNumber}"
            : "";
        return $"- {scope}{subject}{attribution}";
    }

    // Enriches each commit with its associated PR number and author.
    // Uses `gh api` to query the GitHub API — one call per commit.
    // Commits with no associated PR (direct pushes) are returned unchanged.
    // Any individual lookup failure is silently skipped.
    static List<ConventionalCommit> EnrichWithPrInfo(List<ConventionalCommit> commits, string repoPath) {
        return commits.Select(c => {
            try {
                var (stdout, code) = Gh($"api /repos/{repoPath}/commits/{c.Hash}/pulls --jq '.[0] | {{number: .number, login: .user.login}}'");
                if (code != 0 || string.IsNullOrWhiteSpace(stdout) || stdout == "null") return c;

                // Parse the JSON manually — no System.Text.Json in file-based scripts by default
                var numberMatch = System.Text.RegularExpressions.Regex.Match(stdout, @"""number"":\s*(\d+)");
                var loginMatch  = System.Text.RegularExpressions.Regex.Match(stdout, @"""login"":\s*""([^""]+)""");

                if (!numberMatch.Success || !loginMatch.Success) return c;

                return c with {
                    PrNumber = int.Parse(numberMatch.Groups[1].Value),
                    PrAuthor = loginMatch.Groups[1].Value,
                };
            } catch {
                return c;
            }
        }).ToList();
    }

    // Extracts "owner/repo" from the repo URL for use in API calls.
    // https://github.com/kurrent-io/kurrent → kurrent-io/kurrent
    static string? ExtractRepoPath(string? repoUrl) {
        if (repoUrl is null) return null;
        var uri = new Uri(repoUrl);
        return uri.AbsolutePath.TrimStart('/');
    }

    static (string Stdout, int ExitCode) Gh(string arguments) {
        var psi = new System.Diagnostics.ProcessStartInfo("gh", arguments) {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = Directory.GetCurrentDirectory(),
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return (stdout, proc.ExitCode);
    }

    // ─── CHANGELOG.md file management ────────────────────────────────────────
    //
    // File structure (Keep a Changelog 1.1.0):
    //
    //   # Changelog                                    ← written once, never overwritten
    //   (standard blurb)
    //
    //   ## [Unreleased]                                ← always present, empty after release
    //
    //   ## [X.Y.Z] - YYYY-MM-DD                       ← new section prepended here
    //   ### Added
    //   ...
    //
    //   ## [A.B.C] - YYYY-MM-DD                       ← previous sections preserved
    //   ...
    //
    //   [Unreleased]: repo/compare/vX.Y.Z...HEAD      ← reference links, always at bottom
    //   [X.Y.Z]: repo/compare/vA.B.C...vX.Y.Z
    //   [A.B.C]: repo/compare/...

    static void UpdateChangelogFile(
        string path,
        string version,
        string? fromTag,
        string toTag,
        string section,
        string? repoUrl)
    {
        const string fileHeader = """
            # Changelog

            All notable changes to this project will be documented in this file.

            The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
            and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
            """;

        string existingBody  = "";
        string existingLinks = "";

        if (File.Exists(path)) {
            var lines    = File.ReadAllLines(path);
            var linkLine = FindLinksSectionStart(lines);

            existingLinks = linkLine >= 0
                ? string.Join('\n', lines[linkLine..]).Trim()
                : "";

            var bodyLines = linkLine >= 0 ? lines[..linkLine] : lines;
            var firstVer  = Array.FindIndex(bodyLines,
                l => l.TrimStart().StartsWith("## [") && !l.Contains("Unreleased"));

            existingBody = firstVer >= 0
                ? string.Join('\n', bodyLines[firstVer..]).Trim()
                : "";
        }

        var links = BuildReferenceLinks(existingLinks, version, fromTag, toTag, repoUrl);

        var sb = new StringBuilder();
        sb.AppendLine(fileHeader);
        sb.AppendLine();
        sb.AppendLine("## [Unreleased]");
        sb.AppendLine();
        sb.AppendLine(section);

        if (!string.IsNullOrWhiteSpace(existingBody)) {
            sb.AppendLine();
            sb.AppendLine(existingBody);
        }

        sb.AppendLine();
        sb.Append(links);

        File.WriteAllText(path, sb.ToString().TrimEnd() + "\n");
    }

    static string BuildReferenceLinks(
        string existing,
        string version,
        string? fromTag,
        string toTag,
        string? repoUrl)
    {
        // Preserve existing links, excluding Unreleased and the current version
        // (we re-emit both with updated values).
        var preserved = existing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith('['))
            .Where(l => !l.StartsWith("[Unreleased]",     StringComparison.OrdinalIgnoreCase)
                     && !l.StartsWith($"[{version}]:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();

        if (repoUrl is not null) {
            var repo = repoUrl.TrimEnd('/');
            sb.AppendLine($"[Unreleased]: {repo}/compare/{toTag}...HEAD");
            sb.AppendLine(fromTag is not null
                ? $"[{version}]: {repo}/compare/{fromTag}...{toTag}"
                : $"[{version}]: {repo}/releases/tag/{toTag}");
        } else {
            sb.AppendLine($"[Unreleased]:");
            sb.AppendLine($"[{version}]:");
        }

        foreach (var line in preserved)
            sb.AppendLine(line);

        return sb.ToString().TrimEnd();
    }

    // Scans from the bottom up — link lines look like `[tag]: https://...`
    static int FindLinksSectionStart(string[] lines) {
        int linkStart = -1;
        for (int i = lines.Length - 1; i >= 0; i--) {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith('[') && trimmed.Contains("]:"))
                linkStart = i;
            else
                break;
        }
        return linkStart;
    }

    // ─── Display ──────────────────────────────────────────────────────────────

    static void PrintConfig(
        string toTag, string? fromTag, string version,
        string output, string? sectionFile, string? repoUrl, bool dryRun)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]From[/]",         fromTag is not null ? $"[white]{fromTag}[/]" : "[grey]beginning of history[/]");
        grid.AddRow("[grey]To[/]",           $"[white]{toTag}[/]");
        grid.AddRow("[grey]Version[/]",      $"[white]{version}[/]");
        grid.AddRow("[grey]Output[/]",       $"[white]{output}[/]");
        grid.AddRow("[grey]Section file[/]", sectionFile is not null ? $"[white]{sectionFile}[/]" : "[grey]not set[/]");
        grid.AddRow("[grey]Repo URL[/]",     repoUrl is not null ? $"[white]{repoUrl}[/]" : "[grey]not detected[/]");
        grid.AddRow("[grey]Dry run[/]",      dryRun ? "[yellow]true[/]" : "[grey]false[/]");

        AnsiConsole.Write(new Panel(grid)
            .Header("[mediumPurple] Configuration [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.MediumPurple));
        AnsiConsole.WriteLine();
    }
}

// ─── Git ──────────────────────────────────────────────────────────────────────

static class Git {
    public record CommitRaw(string Hash, string ShortHash, string Subject, string Body);

    static (string Stdout, int ExitCode) Run(string arguments) {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments) {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = Directory.GetCurrentDirectory(),
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return (stdout, proc.ExitCode);
    }

    public static string? GetLastTag(string prefix) {
        var (stdout, code) = Run($"describe --tags --match \"{prefix}*.*.*\" --abbrev=0");
        return code == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
    }

    // Returns the tag immediately before `tag` in the ancestry chain.
    public static string? GetPreviousTag(string tag, string prefix) {
        // `git describe --tags --abbrev=0 <tag>^` walks up one commit from the tag
        // and finds the nearest ancestor tag — the previous release.
        var (stdout, code) = Run($"describe --tags --match \"{prefix}*.*.*\" --abbrev=0 \"{tag}^\"");
        return code == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
    }

    public static string? GetRepoUrl() {
        var (stdout, code) = Run("remote get-url origin");
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

        var url = stdout;
        if (url.StartsWith("git@")) {
            var atIdx    = url.IndexOf('@');
            var colonIdx = url.IndexOf(':');
            if (atIdx >= 0 && colonIdx > atIdx)
                url = $"https://{url[(atIdx + 1)..colonIdx]}/{url[(colonIdx + 1)..]}";
        }

        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];

        return url.TrimEnd('/');
    }

    // Fetches commits between fromTag (exclusive) and toTag (inclusive).
    // If fromTag is null, fetches all commits up to toTag.
    public static List<CommitRaw> GetCommitLog(string? fromTag, string? toTag = null) {
        var range = (fromTag, toTag) switch {
            (not null, not null) => $"{fromTag}..{toTag}",
            (not null, null)     => $"{fromTag}..HEAD",
            (null,     not null) => toTag,
            _                    => "HEAD",
        };

        const string sentinel = "CHANGELOG_COMMIT_BOUNDARY";
        var (stdout, _) = Run($"log {range} --format=\"{sentinel}%n%H%n%h%n%s%n%b\"");

        if (string.IsNullOrWhiteSpace(stdout)) return [];

        var commits = new List<CommitRaw>();
        foreach (var block in stdout.Split(sentinel, StringSplitOptions.RemoveEmptyEntries)) {
            var lines = block.Split('\n').Select(l => l.TrimEnd()).ToArray();
            int i = 0;
            while (i < lines.Length && string.IsNullOrEmpty(lines[i])) i++;

            string? hash = null, shortHash = null, subject = null;
            var body = new List<string>();

            if (i < lines.Length) hash      = lines[i++];
            if (i < lines.Length) shortHash = lines[i++];
            if (i < lines.Length) subject   = lines[i++];
            while (i < lines.Length) body.Add(lines[i++]);

            if (hash is null || subject is null) continue;
            commits.Add(new(hash, shortHash ?? hash[..8], subject, string.Join("\n", body).Trim()));
        }

        return commits;
    }
}
