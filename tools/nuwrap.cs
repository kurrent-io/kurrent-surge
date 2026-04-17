#!/usr/bin/env dotnet
#:package Spectre.Console@0.*
#:package System.CommandLine@2.*
#:package NuGet.Versioning@6.*
#:package NuGet.Protocol@6.*
#:package Microsoft.Extensions.Configuration@10.*
#:package Microsoft.Extensions.Configuration.Json@10.*
#:package Microsoft.Extensions.Configuration.Binder@10.*
#:package Microsoft.Extensions.Configuration.EnvironmentVariables@10.*
#:package NetEscapades.Configuration.Yaml@3.*
#:property ToolCommandName=nuwrap

using System.CommandLine;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Spectre.Console;

#region ->> CLI <<-

Option<string?> configOption = new("--config") {
    Description = "Explicit config file path (yml or json)"
};

Option<string[]> sourceOption = new("--source") {
    Description = "NuGet feed URL or local path to read packages from (repeatable)",
    AllowMultipleArgumentsPerToken = true
};
sourceOption.Arity = ArgumentArity.ZeroOrMore;

Option<string[]> packageOption = new("--package") {
    Description = "Package ID to download from remote source (repeatable)",
    AllowMultipleArgumentsPerToken = true
};
packageOption.Arity = ArgumentArity.ZeroOrMore;

Option<string?> targetVersionOption = new("--target-version") {
    Description = "Target version to stamp (e.g. 1.2.3 or 1.2.3-rc.1)"
};

Option<string[]> pushOption = new("--push") {
    Description = "Push target: feed URL or local path (repeatable)",
    AllowMultipleArgumentsPerToken = true
};
pushOption.Arity = ArgumentArity.ZeroOrMore;

Option<bool> forceOption = new("--force") {
    Description = "Skip all validation checks"
};

Option<string?> workDirOption = new("--work-dir") {
    Description = "Working directory for unzip/rezip operations (default: system temp, never cleaned up)"
};

Option<bool> dryRunOption = new("--dry-run") {
    Description = "Validate and preview without side effects"
};

Option<string?> outputOption = new("--output") {
    Description = "Directory for stamped packages (default: system temp)"
};

Option<bool> noProgressOption = new("--no-progress") {
    Description = "Suppress progress output to stderr"
};

var root = new RootCommand("nuwrap — stamp and promote NuGet packages without rebuilding") {
    configOption, sourceOption, packageOption, targetVersionOption, pushOption,
    forceOption, workDirOption, dryRunOption, outputOption, noProgressOption
};

root.SetAction(async (parseResult, ct) => {
    var stderr = SpectreConsole.CreateStderr(parseResult.GetValue(noProgressOption));

    PrintBanner(stderr);

    var config = LoadConfig(
        configPath: parseResult.GetValue(configOption),
        sources:    parseResult.GetValue(sourceOption) ?? [],
        packages:   parseResult.GetValue(packageOption) ?? [],
        version:    parseResult.GetValue(targetVersionOption),
        push:       parseResult.GetValue(pushOption) ?? [],
        force:      parseResult.GetValue(forceOption),
        workDir:    parseResult.GetValue(workDirOption),
        dryRun:     parseResult.GetValue(dryRunOption),
        output:     parseResult.GetValue(outputOption),
        stderr:     stderr
    );

    PrintConfig(config, stderr);

    if (!ValidateConfig(config, stderr)) return 1;

    NuGetVersion.TryParse(config.Version!, out var targetVersion);

    if (targetVersion!.IsPrerelease)
        stderr.MarkupLine($"[yellow]⚠️  '{config.Version}' is a pre-release. Continuing.[/]\n");

    if (config.DryRun) {
        stderr.MarkupLine("[yellow]⚠️  Dry run — no packages will be downloaded, stamped, or pushed[/]\n");
        return await RunDryRun(config, targetVersion, stderr);
    }

    return await RunPromotion(config, targetVersion, stderr);
});

return await root.Parse(args).InvokeAsync();

#endregion

// NOTE: All static local functions must precede type declarations (records/classes)
// to satisfy the C# top-level programs ordering requirement (CS8803).

#region ->> Banner & Config display <<-

static void PrintBanner(IAnsiConsole stderr) {
    stderr.Write(new FigletText("nuwrap").Color(Color.MediumPurple));
    stderr.MarkupLine("[grey]stamp and promote NuGet packages without rebuilding[/]\n");
}

static void PrintConfig(NuWrapConfig config, IAnsiConsole stderr) {
    var grid = new Grid().AddColumn().AddColumn();

    grid.AddRow("[grey]Version[/]",  $"[white]{config.Version ?? "[red]not set[/]"}[/]");
    grid.AddRow("[grey]Work dir[/]",  config.WorkDir is not null ? $"[white]{config.WorkDir}[/]" : "[grey]system temp[/]");
    grid.AddRow("[grey]Output[/]",   $"[white]{config.Output}[/]");
    grid.AddRow("[grey]Force[/]",    config.Force   ? "[red]true[/]"    : "[grey]false[/]");
    grid.AddRow("[grey]Dry run[/]",  config.DryRun  ? "[yellow]true[/]" : "[grey]false[/]");
    grid.AddRow("[grey]Sources[/]",  config.Sources.Count > 0
        ? string.Join("\n", config.Sources.Select(s => $"[white]{s.Url}[/]"))
        : "[grey]none[/]");
    grid.AddRow("[grey]Packages[/]", config.Packages.Count > 0
        ? string.Join(", ", config.Packages.Select(p => $"[white]{p}[/]"))
        : "[grey]none[/]");
    grid.AddRow("[grey]Push[/]",     config.Push.Count > 0
        ? string.Join("\n", config.Push.Select(p => $"[white]{p.Url}[/]"))
        : "[grey]none — stamp only[/]");

    var panel = new Panel(grid)
        .Header("[mediumPurple] Resolved Configuration [/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.MediumPurple);

    stderr.Write(panel);
    stderr.WriteLine();
}

#endregion

#region ->> Config loading <<-

static NuWrapConfig LoadConfig(
    string?      configPath,
    string[]     sources,
    string[]     packages,
    string?      version,
    string[]     push,
    bool         force,
    string?      workDir,
    bool         dryRun,
    string?      output,
    IAnsiConsole stderr
) {
    var resolvedPath = configPath ?? DiscoverConfigFile();

    var builder = new ConfigurationBuilder();
    if (resolvedPath is not null) {
        if (resolvedPath.EndsWith(".yml") || resolvedPath.EndsWith(".yaml"))
            builder.AddYamlFile(resolvedPath, optional: false);
        else
            builder.AddJsonFile(resolvedPath, optional: false);
        stderr.MarkupLine($"[grey]Config loaded from:[/] {resolvedPath}\n");
    }

    builder.AddEnvironmentVariables("NUWRAP_");
    var fileConfig = builder.Build().Get<NuWrapConfig>() ?? new NuWrapConfig();

    return fileConfig with {
        Version  = version  ?? fileConfig.Version,
        WorkDir  = workDir  ?? fileConfig.WorkDir,
        Output   = output   ?? fileConfig.Output ?? Path.Combine(Path.GetTempPath(), $"nuwrap-{Path.GetRandomFileName()}"),
        Force    = force    || fileConfig.Force,
        DryRun   = dryRun   || fileConfig.DryRun,
        Sources  = MergeList(fileConfig.Sources, sources.Length  > 0 ? sources.Select(u  => new SourceConfig(u)) : null),
        Packages = MergeStrings(fileConfig.Packages, packages.Length > 0 ? packages : null),
        Push     = MergeList(fileConfig.Push, push.Length > 0 ? push.Select(u => new PushTarget(u)) : null),
    };
}

static string? DiscoverConfigFile() {
    var dirs = new[] { ".", ".nuwrap", "ci", "build", ".github", AppContext.BaseDirectory };
    var exts = new[] { "yml", "yaml", "json" };
    foreach (var dir in dirs)
        foreach (var ext in exts) {
            var path = Path.Combine(dir, $"nuwrap.{ext}");
            if (File.Exists(path)) return path;
        }
    return null;
}

static List<T> MergeList<T>(List<T> existing, IEnumerable<T>? additions) where T : notnull {
    if (additions is null) return existing;
    var merged = new List<T>(existing);
    foreach (var item in additions)
        if (!merged.Contains(item))
            merged.Add(item);
    return merged;
}

static List<string> MergeStrings(List<string> existing, string[]? additions) {
    if (additions is null) return existing;
    var merged = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
    foreach (var s in additions) merged.Add(s);
    return [.. merged];
}

#endregion

#region ->> Validation <<-

static bool ValidateConfig(NuWrapConfig config, IAnsiConsole stderr) {
    var valid = true;

    if (string.IsNullOrWhiteSpace(config.Version)) {
        stderr.MarkupLine("[red]❌ --target-version is required[/]");
        valid = false;
    } else if (!NuGetVersion.TryParse(config.Version, out _)) {
        stderr.MarkupLine($"[red]❌ Invalid version: '{config.Version}'[/]");
        valid = false;
    }

    if (config.Sources.Count == 0 && config.Push.Count == 0) {
        stderr.MarkupLine("[red]❌ Provide at least one --source or --push target[/]");
        valid = false;
    }

    return valid;
}

#endregion

#region ->> Auth resolution <<-

static (string? Token, bool IsApiKey) ResolveAuth(string url, IAnsiConsole stderr) {
    if (url.Contains("nuget.pkg.github.com"))
        return (Environment.GetEnvironmentVariable("GITHUB_TOKEN"), false);
    if (url.Contains("api.nuget.org"))
        return (Environment.GetEnvironmentVariable("NUGET_TOKEN"), true);
    if (url.Contains("pkgs.dev.azure.com"))
        return (Environment.GetEnvironmentVariable("AZURE_DEVOPS_TOKEN"), false);

    var fallback = Environment.GetEnvironmentVariable("NUWRAP_TOKEN");
    if (fallback is null)
        stderr.MarkupLine($"[yellow]⚠️  No token found for {url} — attempting unauthenticated[/]");
    return (fallback, false);
}

static SourceRepository BuildRepository(string url, IAnsiConsole stderr) {
    var (token, _) = ResolveAuth(url, stderr);
    var source = new PackageSource(url);
    if (token is not null)
        source.Credentials = new PackageSourceCredential(url, "nuwrap", token, true, null);
    return Repository.Factory.GetCoreV3(source);
}

#endregion

#region ->> Source partitioning <<-

static (List<SourceConfig> feeds, List<FileInfo> files) PartitionSources(NuWrapConfig config, IAnsiConsole stderr) {
    // If no sources provided, fall back to push targets as implicit sources in order
    var sources = config.Sources.Count > 0
        ? config.Sources
        : config.Push.Select(p => new SourceConfig(p.Url)).ToList();

    var feeds = sources.Where(s => !IsLocalPath(s.Url)).ToList();
    var files = new List<FileInfo>();
    foreach (var s in sources.Where(s => IsLocalPath(s.Url)))
        files.AddRange(ResolveLocalFiles(s.Url, stderr));
    return (feeds, files);
}

static List<FileInfo> ResolveLocalFiles(string path, IAnsiConsole stderr) {
    if (File.Exists(path) && path.EndsWith(".nupkg")) return [new FileInfo(path)];
    if (Directory.Exists(path)) return [.. new DirectoryInfo(path).GetFiles("*.nupkg")];
    stderr.MarkupLine($"[yellow]⚠️  Source not found: {path}[/]");
    return [];
}

static bool IsLocalPath(string url) =>
    !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

#endregion

#region ->> Package resolution <<-

static ResolvedPackage ParsePackageEntry(string entry) {
    var parts = entry.Split('@', 2);
    if (parts.Length == 2 && NuGetVersion.TryParse(parts[1], out var pinned))
        return new(parts[0].Trim(), pinned);
    return new(entry.Trim(), null);
}

#endregion

#region ->> NuGet operations <<-

static async Task<(NuGetVersion Version, string DownloadPath)?> DownloadLatestPreRelease(
    string packageId, SourceRepository repository, string tempDir) {
    try {
        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>();
        var downloadResource = await repository.GetResourceAsync<DownloadResource>();

        var metadata = await metadataResource.GetMetadataAsync(
            packageId, includePrerelease: true, includeUnlisted: false,
            new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);

        var latest = metadata
            .Select(p => p.Identity.Version)
            .Where(v => v.IsPrerelease)
            .OrderByDescending(v => v)
            .FirstOrDefault();

        if (latest is null) return null;

        var downloadPath = Path.Combine(tempDir, $"{packageId}.{latest}.nupkg");
        var downloadContext = new PackageDownloadContext(new SourceCacheContext());

        using var result = await downloadResource.GetDownloadResourceResultAsync(
            new NuGet.Packaging.Core.PackageIdentity(packageId, latest),
            downloadContext, tempDir, NullLogger.Instance, CancellationToken.None);

        await using var fs = File.Create(downloadPath);
        await result.PackageStream.CopyToAsync(fs);
        return (latest, downloadPath);
    } catch { return null; }
}

static async Task PushToFeed(string url, IEnumerable<FileInfo> packages, IAnsiConsole stderr) {
    var (token, isApiKey) = ResolveAuth(url, stderr);
    var repository = BuildRepository(url, stderr);
    var updateResource = await repository.GetResourceAsync<PackageUpdateResource>();

    foreach (var pkg in packages) {
        try {
            await updateResource.Push(
                [pkg.FullName], null, 30, false,
                _ => isApiKey ? token : null,
                _ => null, false, false, null, NullLogger.Instance);
            stderr.MarkupLine($"  [green]✅[/] {Markup.Escape(pkg.Name)}");
        } catch (Exception ex) {
            stderr.MarkupLine($"  [red]❌ {Markup.Escape(pkg.Name)}: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

#endregion

#region ->> Nuspec manipulation <<-

static XDocument? LoadNuspec(ZipArchive zip) {
    var entry = zip.Entries.FirstOrDefault(e =>
        e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    if (entry is null) return null;
    using var stream = entry.Open();
    return XDocument.Load(stream);
}

static void StampNuspec(XDocument doc, XNamespace ns, string version, HashSet<string> siblingIds) {
    var metadata = doc.Root!.Element(ns + "metadata")!;
    metadata.Element(ns + "version")!.Value = version;
    metadata.Element(ns + "dependencies")
        ?.Descendants(ns + "dependency")
        .Where(d => siblingIds.Contains(d.Attribute("id")?.Value ?? ""))
        .ToList()
        .ForEach(d => d.SetAttributeValue("version", version));
}

#endregion

#region ->> Stamping <<-

static async Task<StampResult> StampPackage(
    FileInfo pkg, string version, NuGetVersion targetVersion,
    DirectoryInfo output, bool force, HashSet<string> siblingIds) {

    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try {
        ZipFile.ExtractToDirectory(pkg.FullName, tempDir);

        var nuspecPath = Directory
            .GetFiles(tempDir, "*.nuspec", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (nuspecPath is null)
            return new(pkg.Name, null, version, false, "No .nuspec found");

        var doc = XDocument.Load(nuspecPath);
        var ns  = doc.Root!.GetDefaultNamespace();
        var metadata = doc.Root.Element(ns + "metadata")!;

        var packageId         = metadata.Element(ns + "id")!.Value;
        var currentVersionStr = metadata.Element(ns + "version")?.Value ?? "unknown";

        if (!force) {
            if (!NuGetVersion.TryParse(currentVersionStr, out var currentVersion))
                return new(packageId, currentVersionStr, version, false,
                    $"Cannot parse current version '{currentVersionStr}'");
            if (targetVersion <= currentVersion)
                return new(packageId, currentVersionStr, version, false,
                    $"'{targetVersion}' is not greater than '{currentVersion}'. Use --force to bypass.");
        }

        StampNuspec(doc, ns, version, siblingIds);
        doc.Save(nuspecPath);

        var outputPath = Path.Combine(output.FullName, $"{packageId}.{version}.nupkg");
        if (File.Exists(outputPath)) File.Delete(outputPath);
        ZipFile.CreateFromDirectory(tempDir, outputPath);

        return new(packageId, currentVersionStr, version, true, null);
    } catch (Exception ex) {
        return new(pkg.Name, null, version, false, ex.Message);
    }
}

static string? ResolvePackageId(FileInfo nupkg) {
    try {
        using var zip = ZipFile.OpenRead(nupkg.FullName);
        var doc = LoadNuspec(zip);
        var ns  = doc?.Root?.GetDefaultNamespace() ?? XNamespace.None;
        return doc?.Root?.Element(ns + "metadata")?.Element(ns + "id")?.Value;
    } catch { return null; }
}

#endregion

#region ->> Promotion run <<-

static async Task<int> RunPromotion(NuWrapConfig config, NuGetVersion targetVersion, IAnsiConsole stderr) {
    var (remoteFeeds, localFiles) = PartitionSources(config, stderr);
    var output = new DirectoryInfo(config.Output!);
    output.Create();

    var allFiles = new List<FileInfo>(localFiles);

    // Download remote packages into temp location first
    var downloadTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(downloadTemp);

    foreach (var feed in remoteFeeds) {
        var repository = BuildRepository(feed.Url, stderr);
        foreach (var packageId in config.Packages) {
            var downloaded = await DownloadLatestPreRelease(packageId, repository, downloadTemp);
            if (downloaded is not null)
                allFiles.Add(new FileInfo(downloaded.Value.DownloadPath));
            else
                stderr.MarkupLine($"[red]❌ '{packageId}' not found on {feed.Url}[/]");
        }
    }

    var siblingIds = allFiles
        .Select(ResolvePackageId)
        .Where(id => id is not null)
        .Select(id => id!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var results = new List<StampResult>();

    await stderr.Progress()
        .AutoClear(false)
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new SpinnerColumn())
        .StartAsync(async ctx => {
            var task = ctx.AddTask("[mediumPurple]Stamping packages[/]", maxValue: allFiles.Count);
            foreach (var file in allFiles) {
                task.Description = $"[mediumPurple]Stamping[/] [grey]{file.Name}[/]";
                var result = await StampPackage(file, config.Version!, targetVersion, output, config.Force, siblingIds);
                results.Add(result);
                task.Increment(1);
            }
        });

    if (Directory.Exists(downloadTemp)) Directory.Delete(downloadTemp, recursive: true);

    PrintSummary(results, output, stderr);

    if (results.All(r => r.Success) && config.Push.Count > 0) {
        var stamped = output.GetFiles("*.nupkg");
        foreach (var push in config.Push) {
            stderr.MarkupLine($"\n[mediumPurple]Pushing to[/] [grey]{push.Url}[/]");
            if (IsLocalPath(push.Url)) {
                var dir = Directory.CreateDirectory(push.Url);
                foreach (var pkg in stamped)
                    File.Copy(pkg.FullName, Path.Combine(dir.FullName, pkg.Name), overwrite: true);
            } else {
                await PushToFeed(push.Url, stamped, stderr);
            }
        }
    }

    return results.Any(r => !r.Success) ? 1 : 0;
}

#endregion

#region ->> Dry run <<-

static async Task<int> RunDryRun(NuWrapConfig config, NuGetVersion targetVersion, IAnsiConsole stderr) {
    var (remoteFeeds, localFiles) = PartitionSources(config, stderr);
    var results = new List<StampResult>();

    var resolvedPackages = config.Packages.Select(ParsePackageEntry).ToList();

    foreach (var package in resolvedPackages) {
        bool found = false;
        foreach (var feed in remoteFeeds) {
            try {
                var repository = BuildRepository(feed.Url, stderr);
                NuGetVersion? version = package.PinnedVersion;

                if (version is null) {
                    var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>();
                    var metadata = await metadataResource.GetMetadataAsync(
                        package.Id, includePrerelease: true, includeUnlisted: false,
                        new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);
                    version = metadata
                        .Select(p => p.Identity.Version)
                        .Where(v => v.IsPrerelease)
                        .OrderByDescending(v => v)
                        .FirstOrDefault();
                }

                if (version is null) continue;

                found = true;
                var valid = config.Force || targetVersion > version;
                results.Add(new(package.Id, version.ToString(), config.Version!, valid,
                    valid ? null : $"'{targetVersion}' is not greater than '{version}'"));
                break;
            } catch (Exception ex) {
                results.Add(new(package.Id, null, config.Version!, false, ex.Message));
                found = true;
                break;
            }
        }
        if (!found)
            results.Add(new(package.Id, null, config.Version!, false, "Not found on any source"));
    }

    foreach (var file in localFiles) {
        try {
            using var zip = ZipFile.OpenRead(file.FullName);
            var doc = LoadNuspec(zip);
            var ns  = doc?.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var metadata = doc?.Root?.Element(ns + "metadata");
            var packageId  = metadata?.Element(ns + "id")?.Value ?? file.Name;
            var currentStr = metadata?.Element(ns + "version")?.Value ?? "unknown";
            NuGetVersion.TryParse(currentStr, out var current);
            var valid = config.Force || current is null || targetVersion > current;
            results.Add(new(packageId, currentStr, config.Version!, valid,
                valid ? null : $"'{targetVersion}' is not greater than '{currentStr}'"));
        } catch (Exception ex) {
            results.Add(new(file.Name, null, config.Version!, false, ex.Message));
        }
    }

    PrintDryRunPlan(results, config, stderr);
    return results.Any(r => !r.Success) ? 1 : 0;
}

static void PrintDryRunPlan(List<StampResult> results, NuWrapConfig config, IAnsiConsole stderr) {
    stderr.Write(new Rule("[mediumPurple]Promotion Plan[/]").RuleStyle("grey"));
    stderr.WriteLine();

    var table = BuildResultsTable(results);
    stderr.Write(table);

    if (config.Push.Count > 0) {
        stderr.WriteLine();
        stderr.MarkupLine("[bold]Push targets:[/]");
        foreach (var push in config.Push)
            stderr.MarkupLine($"  [grey]→[/] {Markup.Escape(push.Url)}");
    }

    stderr.WriteLine();
    stderr.MarkupLine(results.All(r => r.Success)
        ? "[green]✅ All packages ready for promotion[/]"
        : "[red]❌ Some packages have issues — resolve before releasing[/]");
}

#endregion

#region ->> Summary <<-

static void PrintSummary(List<StampResult> results, DirectoryInfo output, IAnsiConsole stderr) {
    stderr.WriteLine();
    stderr.Write(BuildResultsTable(results));
    stderr.WriteLine();

    var succeeded = results.Count(r => r.Success);
    var failed    = results.Count(r => !r.Success);

    if (failed == 0)
        stderr.MarkupLine($"[green]✅ {succeeded} package(s) stamped → {Markup.Escape(output.FullName)}[/]");
    else
        stderr.MarkupLine($"[yellow]⚠️  {succeeded} succeeded,[/] [red]{failed} failed[/]");
}

static Table BuildResultsTable(List<StampResult> results) =>
    results.Aggregate(
        new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.MediumPurple)
            .AddColumn("[bold]Package[/]")
            .AddColumn("[bold]Current Version[/]")
            .AddColumn("[bold]Target Version[/]")
            .AddColumn("[bold]Status[/]"),
        (table, r) => {
            var status = r.Success ? "[green]✅ OK[/]" : $"[red]❌ {Markup.Escape(r.Error ?? "Failed")}[/]";
            table.AddRow(
                Markup.Escape(r.PackageName),
                r.OldVersion is null ? "[grey]unknown[/]" : Markup.Escape(r.OldVersion),
                Markup.Escape(r.NewVersion),
                status);
            return table;
        });

#endregion

// Type declarations follow all local functions (required ordering for top-level programs)

#region ->> Models <<-

record SourceConfig(string Url);
record PushTarget(string Url);

record NuWrapConfig {
    public List<SourceConfig> Sources  { get; set; } = [];
    public List<string>       Packages { get; set; } = [];
    public List<PushTarget>   Push     { get; set; } = [];
    public string?            Version  { get; set; }
    public string?            WorkDir  { get; set; }
    public string?            Output   { get; set; }
    public bool               Force    { get; set; }
    public bool               DryRun   { get; set; }
}

record StampResult(string PackageName, string? OldVersion, string NewVersion, bool Success, string? Error);

record ResolvedPackage(string Id, NuGetVersion? PinnedVersion);

#endregion

#region ->> SpectreConsole <<-

static class SpectreConsole {
    public static IAnsiConsole CreateStderr(bool suppress = false) =>
        AnsiConsole.Create(
            new AnsiConsoleSettings { Out = new AnsiConsoleOutput(suppress ? TextWriter.Null : Console.Error) }
        );
}

#endregion
