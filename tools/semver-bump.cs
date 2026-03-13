#!/usr/bin/env dotnet
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*
#:package Octokit@13.*
#:property ToolCommandName=semver-bump

// ReSharper disable ClassNeverInstantiated.Global

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Git;
using Octokit;
using Spectre.Console;
using Spectre.Console.Cli;
using GitTag = Git.GitTag;
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

var app = new CommandApp();

app.Configure(c => {
        c.SetApplicationName("semver-bump");

        c.AddCommand<CheckCommand>("check")
            .WithDescription("Compute the next version from conventional commits");

        c.AddCommand<BumpCommand>("bump")
            .WithDescription("Compute, tag, and push the next version");
    }
);

return await app.RunAsync(args);

#region Models

public enum BumpType { Major = 1, Minor = 2, Patch = 3 }

public enum OutputFormat { Kv, Json, GitHub }

public partial record SemVer {
    public static readonly SemVer Unspecified = new() {
        Major = 0,
        Minor = 0,
        Patch = 0,
    };

    static readonly Regex SemVerPattern = SemVerRegex();

    public int    Major         { get; private init; }
    public int    Minor         { get; private init; }
    public int    Patch         { get; private init; }
    public string PreRelease    { get; private init; } = "";
    public string BuildMetadata { get; private init; } = "";

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static SemVer Parse(string value) {
        // we will accept a leading "v" but ignore it, as it is common in git tags but not part of the semver spec.
        // We will also ignore any pre-release or build metadata, as we only care about the major, minor, and patch numbers for version bumping.
        var match = SemVerPattern.Match(value.TrimStart('v'));
        return new SemVer {
            Major         = match.Groups["major"].Success ? int.Parse(match.Groups["major"].ValueSpan) : 0,
            Minor         = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].ValueSpan) : 0,
            Patch         = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].ValueSpan) : 0,
            PreRelease    = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : "",
            BuildMetadata = match.Groups["buildmetadata"].Success ? match.Groups["buildmetadata"].Value : ""
        };

    }

    public (SemVer Version, BumpType BumpType) Bump(params GitCommit[] commits) => Bump(this, commits);

    static (SemVer Version, BumpType BumpType) Bump(SemVer version, params GitCommit[] commits) {
        var bumpType = CalculateBumpType(commits);

        var bumpVersion = bumpType switch {
            BumpType.Major => new SemVer {
                Major = version.Major + 1,
                Minor = 0,
                Patch = 0
            },
            BumpType.Minor => version with { Minor = version.Minor + 1, Patch = 0 },
            BumpType.Patch => version with { Patch = version.Patch + 1 }
        };

        return (bumpVersion, bumpType);

        static BumpType CalculateBumpType(GitCommit[] commits) {
            const string feature = "feat";
            return commits.Any(x => x.Conventional.IsBreaking)
                ? BumpType.Major
                : commits.Any(x => x.Conventional.Type == feature)
                    ? BumpType.Minor
                    : BumpType.Patch;
        }
    }

    [GeneratedRegex(@"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<buildmetadata>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")]
    private static partial Regex SemVerRegex();
}

public static class SemVerExtensions {
    public static GitTag ToGitTag(this SemVer version) =>
        GitTag.From($"v{version}");
}

public record SemVerBumpResult {
    public SemVer      FromVersion { get; private init; } = SemVer.Unspecified;
    public SemVer      NextVersion { get; private init; } = SemVer.Unspecified;
    public BumpType    Bump        { get; private init; } = BumpType.Patch;
    public GitCommit[] Commits     { get; private init; } = [];

    public static SemVerBumpResult Calculate(SemVer version, params GitCommit[] commits) {
        var (nextVersion, bumpType) = version.Bump(commits);

        return new SemVerBumpResult {
            FromVersion = version,
            Commits     = commits,
            Bump        = bumpType,
            NextVersion     = nextVersion
        };
    }
}

#endregion

#region Settings

abstract class SharedSettings : CommandSettings {
    [CommandOption("--output"), Description("Output format: kv, json, or github")]
    public OutputFormat? Output { get; init; }

    [CommandOption("--no-progress"), Description("Suppress progress output on stderr")]
    public bool NoProgress { get; init; }
}

sealed class CheckSettings : SharedSettings { }

sealed class BumpSettings : SharedSettings {
    [CommandOption("--changelog"), Description("Generate changelog section and write CHANGELOG.md")]
    public bool Changelog { get; init; }

    [CommandOption("--repo-url"),
     Description("Repository URL for changelog compare links (auto-detected from git remote)")]
    public string? RepoUrl { get; init; }
}

#endregion

#region Commands

sealed class CheckCommand : AsyncCommand<CheckSettings> {
    public override async Task<int> ExecuteAsync(
        CommandContext context, CheckSettings settings, CancellationToken ct
    ) {
        var console  = StderrConsole.Create(settings.NoProgress);

        var git  = new OctokitGitRepository();

        var ctx = await git.GetPreReleaseContext(ct);

        var result = SemVerBumpResult.Calculate(ctx.LastVersion, ctx.Commits);

        ResultWriter.WriteSummaryPanel(result, console);
        await ResultWriter.WriteAsync(result, settings.Output, null, ct);

        return 0;
    }
}

sealed class BumpCommand : AsyncCommand<BumpSettings> {
    public override async Task<int> ExecuteAsync(
        CommandContext context, BumpSettings settings, CancellationToken ct
    ) {
        var console  = StderrConsole.Create(settings.NoProgress);

        IGitRepository git  = new OctokitGitRepository();

        var ctx = await git.GetPreReleaseContext(ct);

        var result = SemVerBumpResult.Calculate(ctx.LastVersion, ctx.Commits);



        // Create and push tag

        var nextTag = result.NextVersion.ToGitTag();

        console.MarkupLine($"\n[mediumpurple]Creating tag[/] [white]{Markup.Escape(nextTag)}[/]");

        try {

            var hash = Environment.GetEnvironmentVariable("GITHUB_SHA");

            if (hash == null) {
                var headCommit = await git.GetHeadCommit(ct);

                if (headCommit is null) {
                    // or maybe there is no head commit yet?
                    console.MarkupLine("[red]Failed to retrieve HEAD commit for tagging[/]");
                    return 1;
                }

                hash = headCommit.Hash;
            }

            await git.CreateTagAsync(nextTag, hash, ct);
        } catch (Exception ex) {
            console.MarkupLine($"[red]Failed to create tag: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        // Generate changelog if requested
        string? changelogSection = null;

        if (settings.Changelog) {
            changelogSection = ChangelogWriter.GenerateReleaseSection(result);

            var changelogPath = Path.Combine(Directory.GetCurrentDirectory(), "CHANGELOG.md");

            await ChangelogWriter.UpdateFileAsync(
                changelogPath, result, changelogSection,
                git.Url, ct
            );

            console.MarkupLine("[grey]Updated[/] [white]CHANGELOG.md[/]");
        }

        ResultWriter.WriteSummaryPanel(result, console);
        await ResultWriter.WriteAsync(result, settings.Output, changelogSection, ct);

        return 0;
    }
}

#endregion

#region Stderr Console

static class StderrConsole {
    public static IAnsiConsole Create(bool suppress) =>
        AnsiConsole.Create(
            new AnsiConsoleSettings { Out = new AnsiConsoleOutput(suppress ? TextWriter.Null : Console.Error) }
        );
}

#endregion

#region Result Writer

static class ResultWriter {


    public static async Task WriteAsync(
        SemVerBumpResult result, OutputFormat? format, string? changelog, CancellationToken ct = default
    ) {
        switch (format) {
            case OutputFormat.Kv: WriteKeyValue(result); break;

            case OutputFormat.Json: WriteJson(result); break;

            case OutputFormat.GitHub: await WriteGitHubAsync(result, changelog, ct); break;

            default: Console.WriteLine(result.NextVersion); break; // WHAT THE ACTUAL FUCK IS THIS?!? CONSOLE WRITE?!?!
        }
    }

    public static void WriteSummaryPanel(SemVerBumpResult result, IAnsiConsole console) {
        var bumpColor = result.Bump switch {
            BumpType.Major => "red",
            BumpType.Minor => "yellow",
            _              => "green"
        };

        console.WriteLine();

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Bump[/]", $"[{bumpColor} bold]{result.Bump}[/]");
        grid.AddRow("[grey]Previous tag[/]", $"[white]{result.FromVersion.ToGitTag() ?? "none"}[/]");
        grid.AddRow("[grey]New tag[/]", $"[white]{result.NextTag}[/]");
        grid.AddRow("[grey]Commits[/]", $"[white]{result.Commits.Count}[/]");

        console.Write(
            new Panel(grid)
                .Header("[mediumpurple] Version [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.MediumPurple)
        );

        console.WriteLine();
    }

    static void WriteKeyValue(SemVerBumpResult result) {
        Console.WriteLine($"version={result.NextVersion}"); // WHAT THE ACTUAL FUCK IS THIS?!? CONSOLE WRITE?!?!
        Console.WriteLine($"tag={result.NextTag}");         // WHAT THE ACTUAL FUCK IS THIS?!? CONSOLE WRITE?!?!
        Console.WriteLine($"bump={result.BumpLabel}");      // WHAT THE ACTUAL FUCK IS THIS?!? CONSOLE WRITE?!?!
    }

    static void WriteJson(SemVerBumpResult result) {
        var version = Escape(result.NextVersion);
        var tag     = Escape(result.NextTag);
        var bump    = Escape(result.BumpLabel);
        Console.WriteLine($"{{\"version\":\"{version}\",\"tag\":\"{tag}\",\"bump\":\"{bump}\"}}");// WHAT THE ACTUAL FUCK IS THIS?!? CONSOLE WRITE?!?!
    }

    static string Escape(string value) => value.Replace("\\", @"\\").Replace("\"", "\\\"");

    static async Task WriteGitHubAsync(SemVerBumpResult result, string? changelog, CancellationToken ct = default) {
        var outputFile  = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        var sb = new StringBuilder();
        sb.AppendLine($"version={result.NextVersion}");
        sb.AppendLine($"tag={result.NextTag}");
        sb.AppendLine($"bump={result.BumpLabel}");

        if (changelog is { }) {
            var delimiter = $"SEMVER_BUMP_{Guid.NewGuid():N}";
            sb.AppendLine($"changelog<<{delimiter}");
            sb.AppendLine(changelog);
            sb.AppendLine(delimiter);
        }

        var outputContent = sb.ToString();

        if (outputFile is { })
            await File.AppendAllTextAsync(outputFile, outputContent, ct);
        else
            Console.Write(outputContent);

        if (summaryFile is { }) {
            var summarySb = new StringBuilder();
            summarySb.AppendLine("| | |");
            summarySb.AppendLine("|---|---|");
            summarySb.AppendLine($"| Bump | `{result.BumpLabel}` |");
            summarySb.AppendLine($"| Previous tag | `{result.PreviousTag ?? "none"}` |");
            summarySb.AppendLine($"| New tag | `{result.NextTag}` |");

            if (changelog is { }) {
                summarySb.AppendLine();
                summarySb.AppendLine("### Changelog");
                summarySb.AppendLine();
                summarySb.AppendLine(changelog);
            }

            await File.AppendAllTextAsync(summaryFile, summarySb.ToString(), ct);
        }
    }
}

#endregion

#region Changelog Writer

static class ChangelogWriter {
    public static string GenerateReleaseSection(SemVerBumpResult result) {
        var sb   = new StringBuilder();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        sb.AppendLine($"## [{result.NextVersion}] - {date}");
        sb.AppendLine();

        var sections = result.Commits
            .GroupBy(GetSectionKey)
            .Where(g => g.Any())
            .OrderBy(g => GetSectionOrder(g.Key));

        foreach (var group in sections) {
            var heading = GetSectionHeading(group.Key);
            var subjectFunc = (Func<ConventionalCommit, string>)(c => c.Description);

            Section(heading, group.ToList(), subjectFunc);
        }

        return sb.ToString().TrimEnd();

        string GetSectionKey(ConventionalCommit c) {
            if (c.IsBreaking) return "Breaking Changes";
            if (c.Type == "feat") return "Added";
            if (c.Type is "perf" or "refactor") return "Changed";
            if (c.Type == "revert") return "Removed";
            if (c is { Type: "fix", Scope: "security" or "auth" }) return "Security";
            if (c.Type == "fix") return "Fixed";
            return "Other"; // fallback, though shouldn't happen with conventional commits
        }

        int GetSectionOrder(string key) =>
            key switch {
                "Breaking Changes" => 0,
                "Added"            => 1,
                "Changed"          => 2,
                "Removed"          => 3,
                "Fixed"            => 4,
                "Security"         => 5,
                _                  => 6
            };

        string GetSectionHeading(string key) => key;

        void Section(
            string                            heading,
            List<ConventionalCommit>          commits,
            Func<ConventionalCommit, string>? subject = null
        ) {
            if (commits.Count == 0)
                return;

            subject ??= c => c.Description;
            sb.AppendLine($"### {heading}");
            sb.AppendLine();

            foreach (var c in commits)
                sb.AppendLine(FormatEntry(c, subject(c)));

            sb.AppendLine();
        }
    }

    static string FormatEntry(ConventionalCommit c, string subject) {
        var scope = c.Scope is { } ? $"**{c.Scope}:** " : "";

        return $"- {scope}{subject}";
    }

    public static async Task UpdateFileAsync(
        string path, SemVerBumpResult result, string section, string? repoUrl, CancellationToken ct = default
    ) {
        const string header = """
            # Changelog

            All notable changes to this project will be documented in this file.

            The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
            and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
            """;

        var existingBody  = "";
        var existingLinks = "";
        var fileExists    = File.Exists(path);

        if (fileExists) {
            var content = await File.ReadAllTextAsync(path, ct);

            var linksSectionIndex = FindLinksSectionStart(content);

            if (linksSectionIndex >= 0) {
                existingLinks = content[linksSectionIndex..].Trim();
                content       = content[..linksSectionIndex];
            }

            var firstVersion = content.IndexOf("\n## ", StringComparison.Ordinal);

            existingBody = firstVersion >= 0
                ? content[(firstVersion + 1)..].Trim()
                : "";
        }

        var newLinks = BuildReferenceLinks(existingLinks, result, repoUrl);

        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine("## [Unreleased]");
        sb.AppendLine();
        sb.AppendLine(section);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(existingBody)) {
            var bodyWithoutUnreleased = RemoveUnreleasedHeader(existingBody);

            if (!string.IsNullOrWhiteSpace(bodyWithoutUnreleased)) {
                sb.AppendLine(bodyWithoutUnreleased);
                sb.AppendLine();
            }
        }

        sb.AppendLine(newLinks);

        await File.WriteAllTextAsync(path, sb.ToString().TrimEnd() + "\n", ct);
    }

    static string BuildReferenceLinks(string existingLinks, SemVerBumpResult result, string? repoUrl) {
        if (repoUrl is null)
            return existingLinks;

        var repo = repoUrl.TrimEnd('/');

        var lines = existingLinks
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith('['))
            .Where(l => !l.StartsWith("[Unreleased]", StringComparison.OrdinalIgnoreCase)
                     && !l.StartsWith($"[{result.NextVersion}]", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"[Unreleased]: {repo}/compare/{result.NextTag}...HEAD");

        if (result.PreviousTag is { })
            sb.AppendLine($"[{result.NextVersion}]: {repo}/compare/{result.PreviousTag}...{result.NextTag}");
        else
            sb.AppendLine($"[{result.NextVersion}]: {repo}/releases/tag/{result.NextTag}");

        foreach (var line in lines)
            sb.AppendLine(line);

        return sb.ToString().TrimEnd();
    }

    static int FindLinksSectionStart(string content) {
        var lines = content.Split('\n');

        for (var i = lines.Length - 1; i >= 0; i--) {
            var line = lines[i].TrimStart();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith('[') && line.Contains("]: http"))
                continue;

            if (i + 1 >= lines.Length)
                return -1;

            var offset = 0;

            for (var j = 0; j <= i; j++)
                offset += lines[j].Length + 1;

            return offset;
        }

        return -1;
    }

    static string RemoveUnreleasedHeader(string body) {
        var lines  = body.Split('\n').ToList();
        var result = new List<string>();
        var skip   = false;

        foreach (var line in lines) {
            if (line.TrimStart().StartsWith("## [Unreleased]", StringComparison.OrdinalIgnoreCase)) {
                skip = true;

                continue;
            }

            if (skip && line.TrimStart().StartsWith("## [") && !line.Contains("Unreleased"))
                skip = false;

            if (!skip)
                result.Add(line);
        }

        return string.Join('\n', result).Trim();
    }
}

#endregion

#region Git

namespace Git {
    public readonly partial record struct GitTag {
        public static readonly GitTag Unspecified = new();

        public static readonly GitTag Head = new() {
            Value = "HEAD",
            Ref   = "HEAD"
        };

        static readonly Regex CommitPattern = GitTagRegex();

        public static GitTag From(string value) {
            if (value.Equals(Head, StringComparison.OrdinalIgnoreCase))
                return Head;

            if (!CommitPattern.IsMatch(value))
                throw new ArgumentException($"Invalid git tag name: '{value}' does not conform to git-check-ref-format rules.");

            return new GitTag {
                Value = value,
                Ref   = $"refs/tags/{value}"
            };
        }

        public string Value { get; private init; }
        public string Ref   { get; private init; }

        public override string ToString() => Value;

        public static implicit operator string(GitTag tag) => tag.Value;

        [GeneratedRegex(@"^(?!-)(?!/)(?!refs/heads/)(?!refs/tags/)(?!refs/remotes/)(?!.*\.\.)(?!.*\.$)(?![0-9a-fA-F]{40}$)[a-zA-Z0-9./_-]{1,250}$")]
        private static partial Regex GitTagRegex();
    }

    public readonly record struct GitCommitStats(int Additions, int Deletions, int Total) {
        public static readonly GitCommitStats Unspecified = new();

        public override string ToString() => $"Stats: +{Additions} -{Deletions} ={Total}";
    }

    public readonly record struct GitCommiter(string Name, string Email) {
        public static readonly GitCommiter Unspecified = new();

        public override string ToString() => $"{Name} <{Email}>";
    }

    public record GitCommit {
        public string         Message    { get; private init; } = "";
        public string         Subject    { get; private init; } = "";
        public string         Body       { get; private init; } = "";
        public string         Hash       { get; private init; } = "";
        public GitCommiter    Author     { get; private init; } = GitCommiter.Unspecified;
        public GitCommiter    Commiter   { get; private init; } = GitCommiter.Unspecified;
        public string         Label      { get; private init; } = "";
        public GitCommitStats Stats      { get; private init; } = GitCommitStats.Unspecified;
        public DateTimeOffset Timestamp  { get; private init; } = DateTimeOffset.MinValue;
        public string         Url        { get; private init; } = "";

        public ConventionalCommit Conventional { get; private init; } = ConventionalCommit.Unspecified;

        public bool IsConventionalCommit => Conventional.Type != "";

        public override string ToString() =>
            IsConventionalCommit
                ? Conventional.ToString()
                : Subject;

        public static GitCommit Parse(GitHubCommit commit) {
            try {
                var nl      = commit.Commit.Message.IndexOf('\n');
                var subject = nl >= 0 ? commit.Commit.Message[..nl].Trim() : commit.Commit.Message.Trim();
                var body    = nl >= 0 ? commit.Commit.Message[(nl + 1)..].Trim() : "";

                return new GitCommit {
                    Message = commit.Commit.Message,
                    Subject = subject,
                    Body = body,
                    Hash = commit.Sha,
                    Author = new GitCommiter(commit.Commit.Author.Name, commit.Commit.Author.Email),
                    Commiter = new GitCommiter(commit.Commit.Committer.Name, commit.Commit.Committer.Email),
                    Label = commit.Commit.Label,
                    Stats = new GitCommitStats(commit.Stats.Additions, commit.Stats.Deletions, commit.Stats.Total),
                    Timestamp = commit.Commit.Author.Date,
                    Url = commit.Commit.Url,
                    Conventional = ConventionalCommit.Parse(subject)
                };
            }
            catch (Exception ex) {
                throw new Exception($"Failed to parse git commit {commit.Sha}", ex);
            }
        }
    }

    public partial record ConventionalCommit {
        public static readonly ConventionalCommit Unspecified = new();

        static readonly Regex CommitPattern = CommitRegex();

        public string   Type        { get; private init; } = "";
        public string   Scope       { get; private init; } = "";
        public string   Description { get; private init; } = "";
        public bool     IsBreaking  { get; private init; }

        string Display { get; init; } = "";

        public static ConventionalCommit Parse(string subject) {
            var match = CommitPattern.Match(subject);

            var type        = match.Groups["type"].Value.ToLowerInvariant();
            var scope       = match.Groups["scope"].Success ? match.Groups["scope"].Value : "";
            var description = match.Groups["description"].Value;
            var isBreaking  = match.Groups["breaking"].Success;

            var display = $"{type}{(scope != "" ? $"({scope})" : "")}{(isBreaking ? "!" : "")}: {description}";

            return match.Success
                ? new ConventionalCommit {
                    Type        = type,
                    Scope       = scope,
                    Description = description,
                    IsBreaking  = isBreaking,
                    Display     = display
                }
                : Unspecified;
        }

        public override string ToString() => Display;

        // <type>([optional scope])[optional breaking !]: <description>
        [GeneratedRegex(@"^(?<type>[\w-]+)(?:\((?<scope>[^)]+)\))?(?<breaking>!)?: (?<description>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex CommitRegex();
    }


    public record PreReleaseContext(GitTag LastTag, SemVer LastVersion, GitCommit[] Commits);

    interface IGitRepository {
        string Url { get; }

        Task CreateTagAsync(GitTag tag, string hash, CancellationToken ct);

        Task<GitTag> GetLastTagAsync(CancellationToken ct);

        Task<GitCommit?> GetCommit(string reference, CancellationToken ct);

        Task<PreReleaseContext> GetPreReleaseContext(CancellationToken ct);

        Task<GitCommit?> GetHeadCommit(CancellationToken ct) => GetCommit(GitTag.Head, ct);
    }

    // TODO SS: why not use repository id instead of owner/repo?
    sealed class OctokitGitRepository : IGitRepository {
        public string Url { get; }

        readonly GitHubClient _client;
        readonly string       _owner;
        readonly string       _repo;

        public OctokitGitRepository(string token, string repository) {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            // this feels stupid must test
            var slash = repository.IndexOf('/');
            _owner = repository[..slash];
            _repo  = repository[(slash + 1)..];

            Url     = $"https://github.com/{_owner}/{_repo}";

            _client = new GitHubClient(new ProductHeaderValue("semver-bump")) { Credentials = new Credentials(token) };
        }

        public OctokitGitRepository() : this(
            Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "") { }

        public async Task CreateTagAsync(GitTag tag, string hash, CancellationToken ct) {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag.Value, nameof(tag));
            ArgumentException.ThrowIfNullOrWhiteSpace(hash);

            if (tag == GitTag.Head)
                throw new ArgumentException("Cannot create a tag with the name 'HEAD'", nameof(tag));

            await _client.Git.Reference.Create(_owner, _repo, new NewReference(tag.Ref, hash));
        }

        public async Task<GitCommit?> GetCommit(string reference, CancellationToken ct) {
            var commit = await _client.Repository.Commit.Get(_owner, _repo, reference);
            return commit is { } ? GitCommit.Parse(commit) : null;
        }

        public async Task<GitTag> GetLastTagAsync(CancellationToken ct) {
            var tags = await _client.Repository.GetAllTags(_owner, _repo);
            return tags.Select(x => GitTag.From(x.Name)).LastOrDefault();
        }

        public async IAsyncEnumerable<GitCommit> GetCommits(GitTag fromTag, GitTag toTag, [EnumeratorCancellation] CancellationToken ct) {
            var head = toTag == GitTag.Unspecified ? GitTag.Head : toTag;

            if (fromTag != GitTag.Unspecified) {
                var comparison = await _client.Repository.Commit.Compare(
                    _owner, _repo, fromTag.Value,
                    head
                );

                foreach (var commit in comparison.Commits)
                    yield return GitCommit.Parse(commit);
            } else {
                var paging = new ApiOptions {
                    StartPage = -1,
                    PageSize  = 200,
                    PageCount = 1
                };

                while (!ct.IsCancellationRequested) {
                    paging.StartPage++;

                    var response = await _client.Repository.Commit.GetAll(_owner, _repo, paging);

                    if (response.Count <= 0)
                        yield break;

                    foreach (var commit in response)
                        yield return GitCommit.Parse(commit);
                }
            }
        }

        public async Task<PreReleaseContext> GetPreReleaseContext(CancellationToken ct) {
            var lastTag = await GetLastTagAsync(ct);
            var commits = await GetCommits(lastTag, GitTag.Unspecified, ct).ToArrayAsync(ct);

            return new PreReleaseContext(lastTag, SemVer.Parse(lastTag), commits);
        }
    }
}


#endregion
