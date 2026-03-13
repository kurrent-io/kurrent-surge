#!/usr/bin/env dotnet
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*
#:package Octokit@13.*
#:property ToolCommandName=semver-bump

// ReSharper disable ClassNeverInstantiated.Global

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Git;
using Octokit;
using Output;
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

sealed class BumpSettings : SharedSettings { }

#endregion

#region Commands

sealed class CheckCommand : AsyncCommand<CheckSettings> {
    public override async Task<int> ExecuteAsync(
        CommandContext context, CheckSettings settings, CancellationToken ct
    ) {
        var stderr = StderrConsole.Create(settings.NoProgress);
        var stdout = StdoutConsole.Create();
        var git    = new OctokitGitRepository();

        var ctx    = await git.GetPreReleaseContext(ct);
        var result = SemVerBumpResult.Calculate(ctx.LastVersion, ctx.Commits);

        var writer = ResultWriterFactory.Create(settings.Output, stderr, stdout);
        await writer.WriteAsync(result, ct);

        return 0;
    }
}

sealed class BumpCommand : AsyncCommand<BumpSettings> {
    public override async Task<int> ExecuteAsync(
        CommandContext context, BumpSettings settings, CancellationToken ct
    ) {
        var stderr = StderrConsole.Create(settings.NoProgress);
        var stdout = StdoutConsole.Create();
        IGitRepository git = new OctokitGitRepository();

        var ctx    = await git.GetPreReleaseContext(ct);
        var result = SemVerBumpResult.Calculate(ctx.LastVersion, ctx.Commits);

        var nextTag = result.NextVersion.ToGitTag();
        stderr.MarkupLine($"\n[mediumpurple]Creating tag[/] [white]{Markup.Escape(nextTag)}[/]");

        try {
            var hash = Environment.GetEnvironmentVariable("GITHUB_SHA")
                ?? (await git.GetHeadCommit(ct))?.Hash;

            if (hash is null) {
                stderr.MarkupLine("[red]Failed to retrieve HEAD commit for tagging[/]");
                return 1;
            }

            await git.CreateTagAsync(nextTag, hash, ct);
        } catch (Exception ex) {
            stderr.MarkupLine($"[red]Failed to create tag: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        var writer = ResultWriterFactory.Create(settings.Output, stderr, stdout);
        await writer.WriteAsync(result, ct);

        return 0;
    }
}

#endregion

#region Output

namespace Output {
    static class StderrConsole {
        public static IAnsiConsole Create(bool suppress) =>
            AnsiConsole.Create(
                new AnsiConsoleSettings {
                    Out = new AnsiConsoleOutput(suppress ? TextWriter.Null : Console.Error)
                }
            );
    }

    static class StdoutConsole {
        public static IAnsiConsole Create() =>
            AnsiConsole.Create(
                new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) }
            );
    }

    interface IResultWriter {
        Task WriteAsync(SemVerBumpResult result, CancellationToken ct);
    }

    abstract class BaseResultWriter(IAnsiConsole stderr, IAnsiConsole stdout) : IResultWriter {
        protected IAnsiConsole Stderr => stderr;
        protected IAnsiConsole Stdout => stdout;

        public async Task WriteAsync(SemVerBumpResult result, CancellationToken ct) {
            WriteSummaryPanel(result);
            await WriteFormatAsync(result, ct);
        }

        protected abstract Task WriteFormatAsync(SemVerBumpResult result, CancellationToken ct);

        void WriteSummaryPanel(SemVerBumpResult result) {
            var bumpColor = result.Bump switch {
                BumpType.Major => "red",
                BumpType.Minor => "yellow",
                _              => "green"
            };

            Stderr.WriteLine();

            var grid = new Grid().AddColumn().AddColumn();
            grid.AddRow("[grey]Bump[/]", $"[{bumpColor} bold]{result.Bump}[/]");
            grid.AddRow("[grey]Previous version[/]",
                result.FromVersion != SemVer.Unspecified
                    ? $"[white]{result.FromVersion}[/]"
                    : "[grey]none[/]");
            grid.AddRow("[grey]New version[/]", $"[white]{result.NextVersion}[/]");
            grid.AddRow("[grey]Commits[/]", $"[white]{result.Commits.Length}[/]");

            Stderr.Write(
                new Panel(grid)
                    .Header("[mediumpurple] Version [/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.MediumPurple)
            );

            Stderr.WriteLine();
        }
    }

    sealed class DefaultResultWriter(IAnsiConsole stderr, IAnsiConsole stdout)
        : BaseResultWriter(stderr, stdout) {
        protected override Task WriteFormatAsync(SemVerBumpResult result, CancellationToken ct) {
            Stdout.WriteLine($"{result.NextVersion}");
            return Task.CompletedTask;
        }
    }

    sealed class KvResultWriter(IAnsiConsole stderr, IAnsiConsole stdout)
        : BaseResultWriter(stderr, stdout) {
        protected override Task WriteFormatAsync(SemVerBumpResult result, CancellationToken ct) {
            Stdout.WriteLine($"version={result.NextVersion}");
            Stdout.WriteLine($"tag={result.NextVersion.ToGitTag()}");
            Stdout.WriteLine($"bump={result.Bump}");
            return Task.CompletedTask;
        }
    }

    sealed class JsonResultWriter(IAnsiConsole stderr, IAnsiConsole stdout)
        : BaseResultWriter(stderr, stdout) {
        protected override Task WriteFormatAsync(SemVerBumpResult result, CancellationToken ct) {
            var version = JsonEncodedText.Encode($"{result.NextVersion}");
            var tag     = JsonEncodedText.Encode($"{result.NextVersion.ToGitTag()}");
            var bump    = JsonEncodedText.Encode($"{result.Bump}");
            Stdout.WriteLine($$"""{"version":"{{version}}","tag":"{{tag}}","bump":"{{bump}}"}""");
            return Task.CompletedTask;
        }
    }

    sealed class GitHubResultWriter(IAnsiConsole stderr, IAnsiConsole stdout)
        : BaseResultWriter(stderr, stdout) {
        protected override async Task WriteFormatAsync(SemVerBumpResult result, CancellationToken ct) {
            var outputFile  = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
            var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

            var previousVersion = result.FromVersion != SemVer.Unspecified
                ? $"{result.FromVersion}"
                : "none";

            var output = $"""
                version={result.NextVersion}
                tag={result.NextVersion.ToGitTag()}
                bump={result.Bump}

                """;

            if (outputFile is not null)
                await File.AppendAllTextAsync(outputFile, output, ct);
            else
                Stdout.Write(output);

            if (summaryFile is not null) {
                var summary = $"""
                    | | |
                    |---|---|
                    | Bump | `{result.Bump}` |
                    | Previous version | `{previousVersion}` |
                    | New version | `{result.NextVersion}` |

                    """;

                await File.AppendAllTextAsync(summaryFile, summary, ct);
            }
        }
    }

    static class ResultWriterFactory {
        public static IResultWriter Create(OutputFormat? format, IAnsiConsole stderr, IAnsiConsole stdout) =>
            format switch {
                OutputFormat.Kv     => new KvResultWriter(stderr, stdout),
                OutputFormat.Json   => new JsonResultWriter(stderr, stdout),
                OutputFormat.GitHub => new GitHubResultWriter(stderr, stdout),
                _                   => new DefaultResultWriter(stderr, stdout)
            };
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

        static readonly Regex TagPattern = GitTagRegex();

        public static GitTag From(string value) {
            if (value.Equals(Head, StringComparison.OrdinalIgnoreCase))
                return Head;

            if (!TagPattern.IsMatch(value))
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

    public readonly record struct GitCommitter(string Name, string Email) {
        public static readonly GitCommitter Unspecified = new();

        public override string ToString() => $"{Name} <{Email}>";
    }

    public record GitCommit {
        public string         Message    { get; private init; } = "";
        public string         Subject    { get; private init; } = "";
        public string         Body       { get; private init; } = "";
        public string         Hash       { get; private init; } = "";
        public GitCommitter    Author     { get; private init; } = GitCommitter.Unspecified;
        public GitCommitter    Commiter   { get; private init; } = GitCommitter.Unspecified;
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
                    Author = commit.Commit.Author is { } a ? new GitCommitter(a.Name, a.Email) : GitCommitter.Unspecified,
                    Commiter = commit.Commit.Committer is { } c ? new GitCommitter(c.Name, c.Email) : GitCommitter.Unspecified,
                    Label = commit.Commit.Label ?? "",
                    Stats = commit.Stats is { } s ? new GitCommitStats(s.Additions, s.Deletions, s.Total) : GitCommitStats.Unspecified,
                    Timestamp = commit.Commit.Author?.Date ?? DateTimeOffset.MinValue,
                    Url = commit.Commit.Url ?? "",
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

            var lastVersion = lastTag != GitTag.Unspecified ? SemVer.Parse(lastTag) : SemVer.Unspecified;
            return new PreReleaseContext(lastTag, lastVersion, commits);
        }
    }
}


#endregion
