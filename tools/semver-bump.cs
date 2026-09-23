#!/usr/bin/env dotnet
#:package Spectre.Console@0.*
#:package System.CommandLine@2.*
#:package Octokit@14.*
#:property ToolCommandName=semver-bump

// ReSharper disable ClassNeverInstantiated.Global

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Git;
using Octokit;
using Spectre.Console;
using GitTag = Git.GitTag;

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

Option<string> tokenOption = new("--token") {
    Description = "GitHub token (defaults to GITHUB_TOKEN env var)",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
    Recursive = true
};

Option<string> repoOption = new("--repo") {
    Description = "GitHub repository as owner/name (defaults to GITHUB_REPOSITORY env var)",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "",
    Recursive = true
};

Option<OutputFormat> outputOption = new("--output") {
    Description = "Output format: envvars, json, or github",
    DefaultValueFactory = _ => OutputFormat.EnvVars,
    Recursive = true
};

Option<bool> noProgressOption = new("--no-progress") {
    Description = "Suppress progress output to stderr",
    Recursive = true
};

Option<string> preReleaseOption = new("--pre-release") {
    Description = "Pre-release identifiers appended verbatim, e.g. \"preview.381\" -> 2.0.0-preview.381; omit for a stable release.",
    DefaultValueFactory = _ => "",
    Recursive = true
};

Option<string> minimumMajorMinorOption = new("--minimum-major-minor") {
    Description = "Version floor as major.minor, e.g. \"2.0\". A computed version below it becomes major.minor.0. Ignored with --version.",
    DefaultValueFactory = _ => "",
    Recursive = true
};

Option<string> versionOption = new("--version") {
    Description = "Explicit version to emit, bypassing git computation (e.g. \"1.2.3\"). Requires no token.",
    DefaultValueFactory = _ => ""
};

Option<string> bumpOption = new("--bump") {
    Description = "Bump a component off the last tag, ignoring commit analysis: major, minor, patch, premajor, preminor, prepatch, or prerelease.",
    DefaultValueFactory = _ => ""
};

var root = new RootCommand("semver-bump") {
    tokenOption, repoOption, outputOption, noProgressOption, preReleaseOption, minimumMajorMinorOption
};

static bool IsValidPreReleaseLabel(string label) =>
    label.Length == 0 || Regex.IsMatch(label, @"^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$");

static bool TryParseFloor(string value, out MajorMinor? floor) {
    floor = null;
    if (value.Length == 0) return true;
    var match = Regex.Match(value, @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)$");
    if (!match.Success) return false;
    floor = new MajorMinor(int.Parse(match.Groups["major"].ValueSpan), int.Parse(match.Groups["minor"].ValueSpan));
    return true;
}

const string PreReleaseId = "preview";

static bool TryParseBumpComponent(string value, out BumpComponent component) {
    component = default;
    return !int.TryParse(value, out _) && Enum.TryParse(value, ignoreCase: true, out component);
}

var checkCommand = new Command("check", "Compute the next version from conventional commits") {
    versionOption, bumpOption
};

checkCommand.SetAction(async (parseResult, ct) => {
    var stderr = SpectreConsole.CreateStderr(parseResult.GetValue(noProgressOption));
    var stdout = SpectreConsole.CreateStdout();

    var explicitVersion = parseResult.GetValue(versionOption)!;
    var bumpValue       = parseResult.GetValue(bumpOption)!;
    var floorValue      = parseResult.GetValue(minimumMajorMinorOption)!;

    if (!TryParseFloor(floorValue, out var floor)) {
        await Console.Error.WriteLineAsync($"Invalid minimum-major-minor '{floorValue}': expected major.minor like 2.0.");
        return 1;
    }

    SemVerBumpResult result;
    if (explicitVersion.Length > 0) {
        if (!SemVer.TryParse(explicitVersion, out var version)) {
            await Console.Error.WriteLineAsync($"Invalid version '{explicitVersion}': expected semver like 1.2.3 or 1.2.3-preview.1.");
            return 1;
        }

        result = SemVerBumpResult.ForVersion(version);
    } else if (bumpValue.Length > 0) {
        if (!TryParseBumpComponent(bumpValue, out var component)) {
            await Console.Error.WriteLineAsync($"Invalid bump component '{bumpValue}': expected major, minor, patch, premajor, preminor, prepatch, or prerelease.");
            return 1;
        }

        IGitClient git = new GitClient(parseResult.GetValue(tokenOption)!, parseResult.GetValue(repoOption)!);
        var ctx        = await git.GetVersioningContext(ct);
        result         = SemVerBumpResult.ForBump(component, ctx.LastStable, ctx.ExistingVersions, PreReleaseId, floor);
    } else {
        var identifiers = parseResult.GetValue(preReleaseOption)!;
        if (!IsValidPreReleaseLabel(identifiers)) {
            await Console.Error.WriteLineAsync($"Invalid pre-release identifiers '{identifiers}': dot-separated alphanumerics and hyphens only.");
            return 1;
        }

        IGitClient git = new GitClient(parseResult.GetValue(tokenOption)!, parseResult.GetValue(repoOption)!);
        var ctx        = await git.GetVersioningContext(ct);
        result         = SemVerBumpResult.Calculate(ctx.LastStable, ctx.Commits, identifiers, floor);
    }

    await ResultWriter
        .Create(parseResult.GetValue(outputOption), stderr, stdout)
        .WriteAsync(result, ct);

    return 0;
});

var bumpCommand = new Command("bump", "Compute the next version and tag it in git");

bumpCommand.SetAction(async (parseResult, ct) => {
    var stderr = SpectreConsole.CreateStderr(parseResult.GetValue(noProgressOption));
    var stdout = SpectreConsole.CreateStdout();

    IGitClient git = new GitClient(parseResult.GetValue(tokenOption)!, parseResult.GetValue(repoOption)!);

    var identifiers = parseResult.GetValue(preReleaseOption)!;
    if (!IsValidPreReleaseLabel(identifiers)) {
        await Console.Error.WriteLineAsync($"Invalid pre-release identifiers '{identifiers}': dot-separated alphanumerics and hyphens only.");
        return 1;
    }

    var floorValue = parseResult.GetValue(minimumMajorMinorOption)!;
    if (!TryParseFloor(floorValue, out var floor)) {
        await Console.Error.WriteLineAsync($"Invalid minimum-major-minor '{floorValue}': expected major.minor like 2.0.");
        return 1;
    }

    var ctx    = await git.GetVersioningContext(ct);
    var result = SemVerBumpResult.Calculate(ctx.LastStable, ctx.Commits, identifiers, floor);

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

    await ResultWriter
        .Create(parseResult.GetValue(outputOption), stderr, stdout)
        .WriteAsync(result, ct);

    return 0;
});

root.Subcommands.Add(checkCommand);
root.Subcommands.Add(bumpCommand);

return await root.Parse(args).InvokeAsync();

#region ->> Models <<-

public enum BumpType { Major = 1, Minor = 2, Patch = 3 }

public enum BumpComponent { Major, Minor, Patch, PreMajor, PreMinor, PrePatch, PreRelease }

public enum OutputFormat { Json, GitHub, EnvVars }

/// <summary>
/// The version floor from minimum-major-minor. A computed version below it becomes major.minor.0.
/// </summary>
public readonly record struct MajorMinor(int Major, int Minor);

public partial record SemVer : IComparable<SemVer> {
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

    public override string ToString() =>
        PreRelease == "" ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public SemVer WithPreRelease(string preRelease) => this with { PreRelease = preRelease };

    public SemVer NextMajor() => this with { Major = Major + 1, Minor = 0, Patch = 0, PreRelease = "" };
    public SemVer NextMinor() => this with { Minor = Minor + 1, Patch = 0, PreRelease = "" };
    public SemVer NextPatch() => this with { Patch = Patch + 1, PreRelease = "" };

    /// <summary>
    /// Raises the version to major.minor.0 when it sits below the floor. Pre-release identifiers are kept.
    /// </summary>
    public SemVer Floor(MajorMinor? floor) {
        if (floor is not { } f) return this;
        var below = Major < f.Major || (Major == f.Major && Minor < f.Minor);
        return below ? this with { Major = f.Major, Minor = f.Minor, Patch = 0 } : this;
    }

    public int CompareTo(SemVer? other) {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public static SemVer Parse(string value) {
        // git tags conventionally prefix "v"
        var match = SemVerPattern.Match(value.TrimStart('v'));
        return new SemVer {
            Major         = match.Groups["major"].Success ? int.Parse(match.Groups["major"].ValueSpan) : 0,
            Minor         = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].ValueSpan) : 0,
            Patch         = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].ValueSpan) : 0,
            PreRelease    = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : "",
            BuildMetadata = match.Groups["buildmetadata"].Success ? match.Groups["buildmetadata"].Value : ""
        };
    }

    public static bool TryParse(string value, out SemVer version) {
        if (SemVerPattern.IsMatch(value.TrimStart('v'))) {
            version = Parse(value);
            return true;
        }

        version = Unspecified;
        return false;
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

    public static SemVerBumpResult ForVersion(SemVer version) =>
        new() { NextVersion = version };

    public static SemVerBumpResult ForBump(
        BumpComponent component, SemVer lastStable, IReadOnlyList<SemVer> existing, string preId, MajorMinor? floor = null
    ) {
        SemVer Pre(SemVer baseVersion) =>
            baseVersion.WithPreRelease($"{preId}.{NextPreReleaseNumber(baseVersion, preId, existing)}");

        // prerelease continues the highest in-flight pre-release line above the last
        // stable; with none in flight it opens a new one off the next patch.
        SemVer OngoingOrNextPatch() =>
            existing
                .Where(v => v.PreRelease != "" && v.CompareTo(lastStable) > 0)
                .OrderByDescending(v => v)
                .Select(v => v.WithPreRelease(""))
                .FirstOrDefault()
            ?? lastStable.NextPatch();

        var (baseVersion, bump, isPre) = component switch {
            BumpComponent.Major      => (lastStable.NextMajor(), BumpType.Major, false),
            BumpComponent.Minor      => (lastStable.NextMinor(), BumpType.Minor, false),
            BumpComponent.Patch      => (lastStable.NextPatch(), BumpType.Patch, false),
            BumpComponent.PreMajor   => (lastStable.NextMajor(), BumpType.Major, true),
            BumpComponent.PreMinor   => (lastStable.NextMinor(), BumpType.Minor, true),
            BumpComponent.PrePatch   => (lastStable.NextPatch(), BumpType.Patch, true),
            BumpComponent.PreRelease => (OngoingOrNextPatch(), BumpType.Patch, true)
        };

        // The floor applies to the base so the preview counter counts tags of the floored line.
        baseVersion = baseVersion.Floor(floor);
        var next    = isPre ? Pre(baseVersion) : baseVersion;

        return new SemVerBumpResult { FromVersion = lastStable, Bump = bump, NextVersion = next };
    }

    public static SemVerBumpResult Calculate(
        SemVer lastStable, GitCommit[] commits, string preReleaseIdentifiers, MajorMinor? floor = null
    ) {
        var (bumpedBase, bumpType) = lastStable.Bump(commits);

        // Identifiers are appended verbatim; the caller owns the counter (CI passes the run number).
        var nextVersion = bumpedBase.Floor(floor).WithPreRelease(preReleaseIdentifiers);

        return new SemVerBumpResult {
            FromVersion = lastStable,
            Commits     = commits,
            Bump        = bumpType,
            NextVersion = nextVersion
        };
    }

    static int NextPreReleaseNumber(SemVer baseVersion, string label, IReadOnlyList<SemVer> existing) {
        var prefix = $"{label}.";

        var highest = existing
            .Where(v => v.Major == baseVersion.Major && v.Minor == baseVersion.Minor && v.Patch == baseVersion.Patch)
            .Where(v => v.PreRelease.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => int.TryParse(v.PreRelease[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return highest + 1;
    }
}

#endregion

#region ->> Output <<-

static class SpectreConsole {
    public static IAnsiConsole CreateStderr(bool suppress = false) =>
        AnsiConsole.Create(
            new AnsiConsoleSettings { Out = new AnsiConsoleOutput(suppress ? TextWriter.Null : Console.Error) }
        );

    public static IAnsiConsole CreateStdout() =>
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out) });
}

interface IResultWriter {
    ValueTask WriteAsync(SemVerBumpResult result, CancellationToken ct);
}

record BumpResult(string Version, string Tag, string Bump);

abstract partial record ResultWriter(IAnsiConsole Stderr, IAnsiConsole Stdout) : IResultWriter {
    public async ValueTask WriteAsync(SemVerBumpResult result, CancellationToken ct) {
        WriteSummaryPanel(result);
        await WriteResultAsync(result, ct);
    }

    protected abstract ValueTask WriteResultAsync(SemVerBumpResult result, CancellationToken ct);

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

sealed record EnvVarsResultWriter(IAnsiConsole Stderr, IAnsiConsole Stdout) : ResultWriter(Stderr, Stdout) {
    protected override ValueTask WriteResultAsync(SemVerBumpResult result, CancellationToken ct) {
        Environment.SetEnvironmentVariable("BUMP_VERSION", result.NextVersion.ToString());
        Environment.SetEnvironmentVariable("BUMP_TAG", result.NextVersion.ToGitTag());
        Environment.SetEnvironmentVariable("BUMP_TYPE", result.Bump.ToString());
        return ValueTask.CompletedTask;
    }
}

sealed record JsonResultWriter(IAnsiConsole Stderr, IAnsiConsole Stdout) : ResultWriter(Stderr, Stdout) {
    protected override async ValueTask WriteResultAsync(SemVerBumpResult result, CancellationToken ct) {
        var output = new BumpResult(result.NextVersion.ToString(), result.NextVersion.ToGitTag(), result.Bump.ToString());
        // Straight to the stream: Spectre wraps at 80 columns when stdout is not a terminal, which
        // splits the JSON across lines for a consumer that reads one line.
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(output, BumpResultJsonContext.Default.BumpResult));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BumpResult))]
partial class BumpResultJsonContext : JsonSerializerContext;

sealed record GitHubResultWriter(IAnsiConsole Stderr, IAnsiConsole Stdout) : ResultWriter(Stderr, Stdout) {
    protected override async ValueTask WriteResultAsync(SemVerBumpResult result, CancellationToken ct) {
        var envFile     = Environment.GetEnvironmentVariable("GITHUB_ENV");
        var outputFile  = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        if (string.IsNullOrWhiteSpace(envFile) || string.IsNullOrWhiteSpace(outputFile) || string.IsNullOrWhiteSpace(summaryFile)) {
            Stderr.MarkupLine("[red bold]Error:[/] [red]GITHUB_ENV and GITHUB_OUTPUT must be set. Are you running inside GitHub Actions?[/]");
            return;
        }

        var envVars = $"""
            BUMP_VERSION={result.NextVersion}
            BUMP_TAG={result.NextVersion.ToGitTag()}
            BUMP_TYPE={result.Bump}

            """;

        var outputs = $"""
            version={result.NextVersion}
            tag={result.NextVersion.ToGitTag()}
            bump={result.Bump}
            is-pre-release={(result.NextVersion.PreRelease.Length > 0 ? "true" : "false")}

            """;

        await File.AppendAllTextAsync(envFile, envVars, ct);
        await File.AppendAllTextAsync(outputFile, outputs, ct);

        var previousVersion = result.FromVersion != SemVer.Unspecified
            ? $"{result.FromVersion}"
            : "none";

        var summary = $"""
            ## Version Bump

            |                  |                                 |
            |------------------|---------------------------------|
            | Bump             | {result.Bump}                   |
            | Previous version | {previousVersion}               |
            | New version      | {result.NextVersion}            |
            | Tag              | {result.NextVersion.ToGitTag()} |
            | Commits          | {result.Commits.Length}         |

            """;

        await File.AppendAllTextAsync(summaryFile, summary, ct);
    }
}

abstract partial record ResultWriter {
    public static IResultWriter Create(OutputFormat format, IAnsiConsole stderr, IAnsiConsole stdout) =>
        format switch {
            OutputFormat.Json   => new JsonResultWriter(stderr, stdout),
            OutputFormat.GitHub => new GitHubResultWriter(stderr, stdout),
            _                   => new EnvVarsResultWriter(stderr, stdout)
        };
}


#endregion

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

        [JsonPropertyName("tag")]
        public string Value { get; private init; }

        [JsonIgnore]
        public string Ref { get; private init; }

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
        public string         Message   { get; private init; } = "";
        public string         Subject   { get; private init; } = "";
        public string         Body      { get; private init; } = "";
        public string         Hash      { get; private init; } = "";
        public GitCommitter   Author    { get; private init; } = GitCommitter.Unspecified;
        public GitCommitter   Commiter  { get; private init; } = GitCommitter.Unspecified;
        public string         Label     { get; private init; } = "";
        public GitCommitStats Stats     { get; private init; } = GitCommitStats.Unspecified;
        public DateTimeOffset Timestamp { get; private init; } = DateTimeOffset.MinValue;
        public string         Url       { get; private init; } = "";

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

    public record VersioningContext(GitTag LastStableTag, SemVer LastStable, GitCommit[] Commits, SemVer[] ExistingVersions);

    interface IGitClient {
        string Url { get; }

        Task CreateTagAsync(GitTag tag, string hash, CancellationToken ct);

        Task<GitCommit?> GetCommit(string reference, CancellationToken ct);

        Task<VersioningContext> GetVersioningContext(CancellationToken ct);

        Task<GitCommit?> GetHeadCommit(CancellationToken ct) => GetCommit(GitTag.Head, ct);
    }

    // TODO SS: why not use repository id instead of owner/repo?
    sealed class GitClient : IGitClient {
        public string Url { get; }

        readonly GitHubClient _client;
        readonly string       _owner;
        readonly string       _repo;

        public GitClient(string token, string repository) {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            // this feels stupid must test
            var slash = repository.IndexOf('/');
            _owner = repository[..slash];
            _repo  = repository[(slash + 1)..];

            Url     = $"https://github.com/{_owner}/{_repo}";

            _client = new GitHubClient(new ProductHeaderValue("semver-bump")) { Credentials = new Credentials(token) };
        }

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

        public async Task<VersioningContext> GetVersioningContext(CancellationToken ct) {
            var versions = (await _client.Repository.GetAllTags(_owner, _repo))
                .Select(x => x.Name)
                .Where(name => SemVer.TryParse(name, out _))
                .Select(name => (Tag: GitTag.From(name), Version: SemVer.Parse(name)))
                .ToArray();

            var stable = versions
                .Where(v => v.Version.PreRelease == "")
                .OrderBy(v => v.Version)
                .ToArray();

            var hasStable        = stable.Length > 0;
            var lastStableTag    = hasStable ? stable[^1].Tag     : GitTag.Unspecified;
            var lastStableVer    = hasStable ? stable[^1].Version : SemVer.Unspecified;

            var commits = await GetCommits(lastStableTag, GitTag.Unspecified, ct).ToArrayAsync(ct);

            return new VersioningContext(
                lastStableTag, lastStableVer, commits, versions.Select(v => v.Version).ToArray()
            );
        }
    }
}
