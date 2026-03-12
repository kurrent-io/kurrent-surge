#!/usr/bin/env dotnet
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*

using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<SemverBumpCommand>();
app.Configure(c => c.SetApplicationName("semver-bump"));
return app.Run(args);

// ─── Models ───────────────────────────────────────────────────────────────────

enum BumpType { Patch, Minor, Major }

enum OutputMode { Text, GitHubActions }

record ConventionalCommit(
    string  Hash,
    string  ShortHash,
    string  Type,
    string? Scope,
    string  Subject,
    bool    IsBreaking,
    string? BreakingNote
);

record BumpResult(
    string?                  PreviousTag,
    string                   PreviousVersion,
    BumpType                 BumpType,
    string                   NextVersion,
    string                   NextTag,
    List<ConventionalCommit> Commits
);

// ─── Settings ─────────────────────────────────────────────────────────────────

sealed class SemverBumpSettings : CommandSettings {
    [CommandOption("--tag-prefix")]
    [Description("Prefix for git tags (default: v)")]
    public string TagPrefix { get; init; } = "v";

    [CommandOption("--dry-run")]
    [Description("Preview version and changelog without creating or pushing any tags")]
    public bool DryRun { get; init; }

    [CommandOption("--push")]
    [Description("Create and push the tag after computing the version")]
    public bool Push { get; init; }

    [CommandOption("--output")]
    [Description("Output mode: text (default) or github-actions")]
    public string Output { get; init; } = "text";

    [CommandOption("--repo-url")]
    [Description("Repository URL for changelog compare links — auto-detected from git remote if omitted")]
    public string? RepoUrl { get; init; }
}

// ─── Command ──────────────────────────────────────────────────────────────────

sealed class SemverBumpCommand : Command<SemverBumpSettings> {
    // Matches: feat(scope)!: subject
    // Groups: type, scope (optional), breaking (optional !), subject
    static readonly Regex CommitPattern = new(
        @"^(?<type>feat|fix|docs|chore|refactor|perf|test|build|ci|revert)(?:\((?<scope>[^)]+)\))?(?<breaking>!)?: (?<subject>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches BREAKING CHANGE: or BREAKING-CHANGE: in commit body/footer
    static readonly Regex BreakingFooterPattern = new(
        @"^BREAKING[- ]CHANGE: (?<note>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public override int Execute(CommandContext context, SemverBumpSettings settings, CancellationToken cancellation = default) {
        var mode = settings.Output.ToLowerInvariant() == "github-actions"
            ? OutputMode.GitHubActions
            : OutputMode.Text;

        if (mode == OutputMode.Text) {
            AnsiConsole.Write(new FigletText("semver-bump").Color(Color.MediumPurple));
            AnsiConsole.MarkupLine("[grey]conventional commits → next version + changelog[/]\n");
        }

        // ── Resolve current version from latest tag ──────────────────────────
        var lastTag         = Git.GetLastTag(settings.TagPrefix);
        var previousVersion = lastTag is not null
            ? lastTag[settings.TagPrefix.Length..]
            : "0.0.0";

        if (mode == OutputMode.Text) {
            AnsiConsole.MarkupLine(lastTag is not null
                ? $"[grey]Last tag:[/] [white]{lastTag}[/]"
                : "[grey]No previous tag found — starting from 0.0.0[/]");
        }

        // ── Parse commits since last tag ─────────────────────────────────────
        var rawCommits = Git.GetCommitLog(lastTag);
        var commits    = rawCommits
            .Select(c => ParseCommit(c.Hash, c.ShortHash, c.Subject, c.Body))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        if (mode == OutputMode.Text)
            AnsiConsole.MarkupLine(
                $"[grey]Commits since last tag:[/] {rawCommits.Count} total, " +
                $"[white]{commits.Count}[/] conventional\n");

        if (commits.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]⚠  No conventional commits since last tag — nothing to release[/]");
            return 0;
        }

        // ── Calculate bump and next version ──────────────────────────────────
        var bumpType = CalculateBump(commits);

        if (!TryParseSemver(previousVersion, out var major, out var minor, out var patch)) {
            AnsiConsole.MarkupLine($"[red]❌ Cannot parse previous version: {Markup.Escape(previousVersion)}[/]");
            return 1;
        }

        var (nextMajor, nextMinor, nextPatch) = bumpType switch {
            BumpType.Major => (major + 1, 0,         0),
            BumpType.Minor => (major,     minor + 1, 0),
            _              => (major,     minor,     patch + 1),
        };

        var nextVersion = $"{nextMajor}.{nextMinor}.{nextPatch}";
        var nextTag     = $"{settings.TagPrefix}{nextVersion}";

        var result = new BumpResult(lastTag, previousVersion, bumpType, nextVersion, nextTag, commits);

        // ── Generate changelog (Keep a Changelog 1.1.0) ──────────────────────
        var section = GenerateReleaseSection(result);

        // ── Emit output ───────────────────────────────────────────────────────
        if (mode == OutputMode.Text)
            PrintTextResult(result, section, settings.DryRun, settings.Push);
        else
            WriteGitHubActionsOutput(result, section);

        if (settings.DryRun) {
            if (mode == OutputMode.Text)
                AnsiConsole.MarkupLine("\n[yellow]Dry run — no tag created or pushed[/]");
            return 0;
        }

        // ── Write CHANGELOG.md ────────────────────────────────────────────────
        var repoUrl       = settings.RepoUrl ?? Git.GetRepoUrl();
        var changelogPath = Path.Combine(Directory.GetCurrentDirectory(), "CHANGELOG.md");
        UpdateChangelogFile(changelogPath, result, section, repoUrl);
        if (mode == OutputMode.Text)
            AnsiConsole.MarkupLine($"[grey]Updated[/] [white]CHANGELOG.md[/]");

        // ── Create and push tag ───────────────────────────────────────────────
        if (settings.Push) {
            if (mode == OutputMode.Text)
                AnsiConsole.MarkupLine($"\n[mediumPurple]Creating tag[/] [white]{Markup.Escape(nextTag)}[/]");

            var tagResult = Git.CreateTag(nextTag);
            if (!tagResult.Success) {
                AnsiConsole.MarkupLine($"[red]❌ Failed to create tag: {Markup.Escape(tagResult.Error ?? "unknown error")}[/]");
                return 1;
            }

            if (mode == OutputMode.Text)
                AnsiConsole.MarkupLine($"[mediumPurple]Pushing tag[/] [white]{Markup.Escape(nextTag)}[/]");

            var pushResult = Git.PushTag(nextTag);
            if (!pushResult.Success) {
                // Tag exists locally — roll it back before failing so the caller
                // doesn't have to clean up.
                Git.DeleteLocalTag(nextTag);
                AnsiConsole.MarkupLine($"[red]❌ Failed to push tag: {Markup.Escape(pushResult.Error ?? "unknown error")}[/]");
                return 1;
            }

            if (mode == OutputMode.Text)
                AnsiConsole.MarkupLine($"[green]✅ Tag [white]{Markup.Escape(nextTag)}[/] pushed[/]");
        }

        return 0;
    }

    // ─── Commit parsing ───────────────────────────────────────────────────────

    ConventionalCommit? ParseCommit(string hash, string shortHash, string subject, string body) {
        var match = CommitPattern.Match(subject);
        if (!match.Success) return null;

        var type  = match.Groups["type"].Value.ToLowerInvariant();
        var scope = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
        var subj  = match.Groups["subject"].Value;
        var hasBang = match.Groups["breaking"].Success;

        var footerMatch = BreakingFooterPattern.Match(body);
        var isBreaking  = hasBang || footerMatch.Success;
        var breakingNote = footerMatch.Success ? footerMatch.Groups["note"].Value : null;

        return new ConventionalCommit(hash, shortHash, type, scope, subj, isBreaking, breakingNote);
    }

    // ─── Bump calculation ─────────────────────────────────────────────────────

    static BumpType CalculateBump(List<ConventionalCommit> commits) {
        if (commits.Any(c => c.IsBreaking))     return BumpType.Major;
        if (commits.Any(c => c.Type == "feat")) return BumpType.Minor;
        return BumpType.Patch;
    }

    // ─── Changelog generation (Keep a Changelog 1.1.0) ───────────────────────
    //
    // Format: https://keepachangelog.com/en/1.1.0/
    //
    // Conventional commit → section mapping:
    //   feat              → Added
    //   fix               → Fixed
    //   perf, refactor    → Changed
    //   revert            → Removed
    //   breaking (any)    → ⚠ Breaking Changes  (prepended before all sections)
    //   docs, chore, test, build, ci → omitted (noise for end users)
    //
    // Compare links are reference-style at the bottom of the file, not inline.
    // This method returns only the section markdown (no file header, no links).
    // Call UpdateChangelogFile to write the full file.

    static string GenerateReleaseSection(BumpResult result) {
        var sb   = new StringBuilder();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Keep a Changelog header format — link is reference-style, resolved at file level
        sb.AppendLine($"## [{result.NextVersion}] - {date}");
        sb.AppendLine();

        var breaking = result.Commits.Where(c => c.IsBreaking).ToList();
        var added    = result.Commits.Where(c => c.Type == "feat"                        && !c.IsBreaking).ToList();
        var changed  = result.Commits.Where(c => c.Type is "perf" or "refactor"          && !c.IsBreaking).ToList();
        var removed  = result.Commits.Where(c => c.Type == "revert"                      && !c.IsBreaking).ToList();
        var fixed_   = result.Commits.Where(c => c.Type == "fix"                         && !c.IsBreaking).ToList();
        // Security: fix commits scoped to "security" or "auth" surface here instead of Fixed
        var security = fixed_.Where(c => c.Scope is "security" or "auth").ToList();
        fixed_       = fixed_.Except(security).ToList();

        void Section(string heading, List<ConventionalCommit> commits, Func<ConventionalCommit, string>? subject = null) {
            if (commits.Count == 0) return;
            subject ??= c => c.Subject;
            sb.AppendLine($"### {heading}");
            sb.AppendLine();
            foreach (var c in commits)
                sb.AppendLine(FormatEntry(c, subject(c)));
            sb.AppendLine();
        }

        Section("⚠ Breaking Changes", breaking, c => c.BreakingNote ?? c.Subject);
        Section("Added",              added);
        Section("Changed",            changed);
        Section("Removed",            removed);
        Section("Fixed",              fixed_);
        Section("Security",           security);

        return sb.ToString().TrimEnd();
    }

    // Keep a Changelog uses plain list items — scope in bold prefix, no commit hash inline.
    // Commit hashes would be noise for end users; they belong in the git log, not the changelog.
    static string FormatEntry(ConventionalCommit c, string subject) {
        var scope = c.Scope is not null ? $"**{c.Scope}:** " : "";
        return $"- {scope}{subject}";
    }

    // Writes or updates CHANGELOG.md following the Keep a Changelog 1.1.0 structure:
    //
    //   # Changelog                          ← written once
    //   (standard blurb)                     ← written once
    //
    //   ## [Unreleased]                      ← always present, empty after release
    //
    //   ## [X.Y.Z] - YYYY-MM-DD             ← prepended on each release
    //   ### Added
    //   ...
    //
    //   [Unreleased]: repo/compare/vX.Y.Z...HEAD   ← reference links, bottom of file
    //   [X.Y.Z]: repo/compare/vPREV...vX.Y.Z
    //
    static void UpdateChangelogFile(string path, BumpResult result, string section, string? repoUrl) {
        const string header = """
            # Changelog

            All notable changes to this project will be documented in this file.

            The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
            and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
            """;

        string existingBody    = "";
        string existingLinks   = "";
        bool   fileExists      = File.Exists(path);

        if (fileExists) {
            var content = File.ReadAllText(path);

            // Split off the reference links block at the bottom.
            // Links section starts at the first line matching `[word]:` pattern.
            var linksSectionIndex = FindLinksSectionStart(content);
            if (linksSectionIndex >= 0) {
                existingLinks = content[linksSectionIndex..].Trim();
                content       = content[..linksSectionIndex];
            }

            // Strip the file header — we'll re-emit it.
            // Find the first ## heading and take everything from there.
            var firstVersion = content.IndexOf("\n## ", StringComparison.Ordinal);
            existingBody = firstVersion >= 0
                ? content[(firstVersion + 1)..].Trim()
                : "";
        }

        // Build updated reference links
        var newLinks = BuildReferenceLinks(existingLinks, result, repoUrl);

        // Compose the full file
        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine("## [Unreleased]");
        sb.AppendLine();

        // New release section
        sb.AppendLine(section);
        sb.AppendLine();

        // Previous content (minus old [Unreleased] header — we just re-emitted it above)
        if (!string.IsNullOrWhiteSpace(existingBody)) {
            var bodyWithoutUnreleased = RemoveUnreleasedHeader(existingBody);
            if (!string.IsNullOrWhiteSpace(bodyWithoutUnreleased)) {
                sb.AppendLine(bodyWithoutUnreleased);
                sb.AppendLine();
            }
        }

        // Reference links at the bottom
        sb.AppendLine(newLinks);

        File.WriteAllText(path, sb.ToString().TrimEnd() + "\n");
    }

    static string BuildReferenceLinks(string existingLinks, BumpResult result, string? repoUrl) {
        if (repoUrl is null) return existingLinks;

        var repo = repoUrl.TrimEnd('/');
        var lines = existingLinks
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith('['))
            // Drop old [Unreleased] and the new version if it already exists (re-emit below)
            .Where(l => !l.StartsWith("[Unreleased]", StringComparison.OrdinalIgnoreCase)
                     && !l.StartsWith($"[{result.NextVersion}]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();

        // [Unreleased] always points from new tag to HEAD
        sb.AppendLine($"[Unreleased]: {repo}/compare/{result.NextTag}...HEAD");

        // New version compare link
        if (result.PreviousTag is not null)
            sb.AppendLine($"[{result.NextVersion}]: {repo}/compare/{result.PreviousTag}...{result.NextTag}");
        else
            sb.AppendLine($"[{result.NextVersion}]: {repo}/releases/tag/{result.NextTag}");

        foreach (var line in lines)
            sb.AppendLine(line);

        return sb.ToString().TrimEnd();
    }

    static int FindLinksSectionStart(string content) {
        var lines = content.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--) {
            var line = lines[i].TrimStart();
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Reference link lines look like: [tag]: https://...
            if (line.StartsWith('[') && line.Contains("]: http")) continue;
            // First non-link, non-blank line from the bottom — links start after this.
            // Compute the character offset by summing preceding line lengths.
            if (i + 1 >= lines.Length) return -1;
            var offset = 0;
            for (int j = 0; j <= i; j++)
                offset += lines[j].Length + 1; // +1 for the '\n'
            return offset;
        }
        return -1;
    }

    static string RemoveUnreleasedHeader(string body) {
        var lines  = body.Split('\n').ToList();
        var result = new List<string>();
        bool skip  = false;

        foreach (var line in lines) {
            if (line.TrimStart().StartsWith("## [Unreleased]", StringComparison.OrdinalIgnoreCase)) {
                skip = true;
                continue;
            }
            // Stop skipping at next ## heading (next version section)
            if (skip && line.TrimStart().StartsWith("## [") && !line.Contains("Unreleased")) {
                skip = false;
            }
            if (!skip) result.Add(line);
        }

        return string.Join('\n', result).Trim();
    }

    // ─── Text output (Spectre) ────────────────────────────────────────────────

    static void PrintTextResult(BumpResult result, string section, bool dryRun, bool push) {
        var bumpColor = result.BumpType switch {
            BumpType.Major => "red",
            BumpType.Minor => "yellow",
            _              => "green",
        };

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Release type[/]",  $"[{bumpColor} bold]{result.BumpType.ToString().ToLower()}[/]");
        grid.AddRow("[grey]Previous tag[/]",  $"[white]{result.PreviousTag ?? "none"}[/]");
        grid.AddRow("[grey]New tag[/]",        $"[white]{result.NextTag}[/]");
        grid.AddRow("[grey]Commits[/]",        $"[white]{result.Commits.Count}[/] conventional");
        grid.AddRow("[grey]Dry run[/]",        dryRun ? "[yellow]true[/]"  : "[grey]false[/]");
        grid.AddRow("[grey]Push tag[/]",       push   ? "[green]true[/]"   : "[grey]false[/]");

        AnsiConsole.Write(new Panel(grid)
            .Header("[mediumPurple] Release Summary [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.MediumPurple));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[mediumPurple]Changelog Section[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(section);
        AnsiConsole.WriteLine();
    }

    // ─── GitHub Actions output ────────────────────────────────────────────────

    static void WriteGitHubActionsOutput(BumpResult result, string section) {
        var outputFile  = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        var delimiter = $"SEMVER_BUMP_{Guid.NewGuid():N}";

        var outputContent = $"""
            release_type={result.BumpType.ToString().ToLower()}
            previous_tag={result.PreviousTag ?? ""}
            previous_version={result.PreviousVersion}
            new_tag={result.NextTag}
            new_version={result.NextVersion}
            changelog<<{delimiter}
            {section}
            {delimiter}

            """;

        if (outputFile is not null)
            File.AppendAllText(outputFile, outputContent);
        else
            Console.Write(outputContent);

        if (summaryFile is not null) {
            var bumpEmoji = result.BumpType switch {
                BumpType.Major => "🔴",
                BumpType.Minor => "🟡",
                _              => "🟢",
            };

            var summaryContent = $"""
                | | |
                |---|---|
                | Release type | {bumpEmoji} `{result.BumpType.ToString().ToLower()}` |
                | Previous tag | `{result.PreviousTag ?? "none"}` |
                | New tag      | `{result.NextTag}` |

                ### Changelog

                {section}

                """;

            File.AppendAllText(summaryFile, summaryContent);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    static bool TryParseSemver(string version, out int major, out int minor, out int patch) {
        major = minor = patch = 0;
        // Strip any pre-release or build metadata — we only care about the numeric core
        var core  = version.Split(['-', '+'], 2)[0];
        var parts = core.Split('.');
        if (parts.Length < 3) return false;
        return int.TryParse(parts[0], out major)
            && int.TryParse(parts[1], out minor)
            && int.TryParse(parts[2], out patch);
    }
}

// ─── Git ──────────────────────────────────────────────────────────────────────

static class Git {
    public record CommitRaw(string Hash, string ShortHash, string Subject, string Body);
    public record GitResult(bool Success, string? Error);

    static (string Stdout, string Stderr, int ExitCode) Run(string arguments) {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments) {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = Directory.GetCurrentDirectory(),
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        // Read both streams concurrently to avoid deadlock when a pipe buffer fills
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdout     = proc.StandardOutput.ReadToEnd().Trim();
        var stderr     = stderrTask.GetAwaiter().GetResult().Trim();
        proc.WaitForExit();
        return (stdout, stderr, proc.ExitCode);
    }

    public static string? GetLastTag(string prefix) {
        var (stdout, _, exitCode) = Run($"describe --tags --match \"{prefix}*.*.*\" --abbrev=0");
        return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
    }

    public static List<CommitRaw> GetCommitLog(string? sinceTag) {
        var range = sinceTag is not null ? $"{sinceTag}..HEAD" : "HEAD";

        // Use a unique per-run sentinel so we can split commits reliably regardless
        // of what's in commit messages. %x00 (null byte) is the cleanest delimiter
        // but some tools strip it — using a long unique string is safer.
        const string sentinel = "SEMVER_BUMP_COMMIT_BOUNDARY";
        var (stdout, _, _) = Run($"log {range} --format=\"{sentinel}%n%H%n%h%n%s%n%b\"");

        if (string.IsNullOrWhiteSpace(stdout)) return [];

        var commits = new List<CommitRaw>();
        var blocks  = stdout.Split(sentinel, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks) {
            var lines = block.Split('\n');

            // Layout after splitting on sentinel newline:
            //   [0] ""  (empty — the \n after sentinel)
            //   [1] full hash
            //   [2] short hash
            //   [3] subject
            //   [4..] body lines

            var nonEmpty = lines.Select(l => l.TrimEnd()).ToArray();

            string? hash = null, shortHash = null, subject = null;
            var bodyLines = new List<string>();
            var lineIdx = 0;

            // Skip leading blank lines
            while (lineIdx < nonEmpty.Length && string.IsNullOrEmpty(nonEmpty[lineIdx]))
                lineIdx++;

            if (lineIdx < nonEmpty.Length) hash      = nonEmpty[lineIdx++];
            if (lineIdx < nonEmpty.Length) shortHash = nonEmpty[lineIdx++];
            if (lineIdx < nonEmpty.Length) subject   = nonEmpty[lineIdx++];

            while (lineIdx < nonEmpty.Length)
                bodyLines.Add(nonEmpty[lineIdx++]);

            if (hash is null || subject is null) continue;

            commits.Add(new(hash, shortHash ?? hash[..8], subject, string.Join("\n", bodyLines).Trim()));
        }

        return commits;
    }

    public static string? GetRepoUrl() {
        var (stdout, _, exitCode) = Run("remote get-url origin");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

        var url = stdout;

        // SSH → HTTPS: git@github.com:org/repo.git → https://github.com/org/repo
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

    public static GitResult CreateTag(string tag) {
        var (_, stderr, exitCode) = Run($"tag {tag}");
        return new(exitCode == 0, exitCode != 0 ? stderr : null);
    }

    public static GitResult PushTag(string tag) {
        var (_, stderr, exitCode) = Run($"push origin {tag}");
        return new(exitCode == 0, exitCode != 0 ? stderr : null);
    }

    public static void DeleteLocalTag(string tag) =>
        Run($"tag -d {tag}");
}
