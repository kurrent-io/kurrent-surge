#!/usr/bin/env dotnet-run
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*
#:package NuGet.Versioning@6.*
#:package NuGet.Protocol@6.*
#:package Microsoft.Extensions.Configuration@9.*
#:package Microsoft.Extensions.Configuration.Json@9.*
#:package Microsoft.Extensions.Configuration.Binder@9.*
#:package NetEscapades.Configuration.Yaml@0.*

using System.ComponentModel;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<NuWrapCommand>();
app.Configure(c => c.SetApplicationName("nuwrap"));
return app.Run(args);

// --- Models ---

record SourceConfig(string Url);
record PushTarget(string Url);

record NuWrapConfig {
    public List<SourceConfig> Sources  { get; init; } = [];
    public List<string>       Packages { get; init; } = [];
    public List<PushTarget>   Push     { get; init; } = [];
    public string?            Version  { get; init; }
    public string?            WorkDir  { get; init; }
    public bool               Force    { get; init; }
    public bool               DryRun   { get; init; }
}

record StampResult(string PackageName, string? OldVersion, string NewVersion, bool Success, string? Error);

// --- Settings ---

sealed class NuWrapSettings : CommandSettings {
    [CommandOption("--config")]
    [Description("Explicit config file path (yml or json)")]
    public string? Config { get; init; }

    [CommandOption("--source")]
    [Description("NuGet feed URL or local path to read packages from (repeatable)")]
    public string[]? Sources { get; init; }

    [CommandOption("--package")]
    [Description("Package ID to download from remote source (repeatable)")]
    public string[]? Packages { get; init; }

    [CommandOption("--version")]
    [Description("Target version to stamp (e.g. 1.2.3 or 1.2.3-rc.1)")]
    public string? Version { get; init; }

    [CommandOption("--push")]
    [Description("Push target: feed URL or local path (repeatable)")]
    public string[]? Push { get; init; }

    [CommandOption("--force")]
    [Description("Skip all validation checks")]
    public bool Force { get; init; }

    [CommandOption("--work-dir")]
    [Description("Working directory for unzip/rezip operations (default: system temp, never cleaned up)")]
    public string? WorkDir { get; init; }

    [CommandOption("--dry-run")]
    [Description("Validate and preview without side effects")]
    public bool DryRun { get; init; }
}

// --- Command ---

sealed class NuWrapCommand : AsyncCommand<NuWrapSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, NuWrapSettings settings) {
        PrintBanner();

        var config = LoadConfig(settings);
        PrintConfig(config);

        if (!ValidateConfig(config)) return 1;

        NuGetVersion.TryParse(config.Version!, out var targetVersion);

        if (targetVersion!.IsPrerelease)
            AnsiConsole.MarkupLine($"[yellow]⚠️  '{config.Version}' is a pre-release. Continuing.[/]\n");

        if (config.DryRun) {
            AnsiConsole.MarkupLine("[yellow]⚠️  Dry run — no packages will be downloaded, stamped, or pushed[/]\n");
            return await RunDryRun(config, targetVersion);
        }

        return await RunPromotion(config, targetVersion);
    }
}

// --- Banner & Config display ---

static void PrintBanner() {
    AnsiConsole.Write(new FigletText("nuwrap").Color(Color.MediumPurple));
    AnsiConsole.MarkupLine("[grey]stamp and promote NuGet packages without rebuilding[/]\n");
}

static void PrintConfig(NuWrapConfig config) {
    var grid = new Grid().AddColumn().AddColumn();

    grid.AddRow("[grey]Version[/]",  $"[white]{config.Version ?? "[red]not set[/]"}[/]");
    grid.AddRow("[grey]Work dir[/]",  config.WorkDir is not null ? $"[white]{config.WorkDir}[/]" : "[grey]system temp[/]");
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

    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
}

// --- Working directory ---

static string ResolveWorkDir(NuWrapConfig config) =>
    config.WorkDir ?? Path.Combine(Path.GetTempPath(), $"nuwrap-{Path.GetRandomFileName()}");

// --- Config loading ---

static NuWrapConfig LoadConfig(NuWrapSettings settings) {
    var configPath = settings.Config ?? DiscoverConfigFile();

    var builder = new ConfigurationBuilder();
    if (configPath is not null) {
        if (configPath.EndsWith(".yml") || configPath.EndsWith(".yaml"))
            builder.AddYamlFile(configPath, optional: false);
        else
            builder.AddJsonFile(configPath, optional: false);
        AnsiConsole.MarkupLine($"[grey]Config loaded from:[/] {configPath}\n");
    }

    builder.AddEnvironmentVariables("NUWRAP_");
    var fileConfig = builder.Build().Get<NuWrapConfig>() ?? new NuWrapConfig();

    return fileConfig with {
        Version  = settings.Version ?? fileConfig.Version,
        WorkDir  = settings.WorkDir ?? fileConfig.WorkDir,
        Force    = settings.Force   || fileConfig.Force,
        DryRun   = settings.DryRun  || fileConfig.DryRun,
        Sources  = MergeList(fileConfig.Sources, settings.Sources?.Select(u => new SourceConfig(u))),
        Packages = MergeStrings(fileConfig.Packages, settings.Packages),
        Push     = MergeList(fileConfig.Push, settings.Push?.Select(u => new PushTarget(u))),
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

// --- Validation ---

static bool ValidateConfig(NuWrapConfig config) {
    var valid = true;

    if (string.IsNullOrWhiteSpace(config.Version)) {
        AnsiConsole.MarkupLine("[red]❌ --version is required[/]");
        valid = false;
    } else if (!NuGetVersion.TryParse(config.Version, out _)) {
        AnsiConsole.MarkupLine($"[red]❌ Invalid version: '{config.Version}'[/]");
        valid = false;
    }

    if (config.Sources.Count == 0 && config.Push.Count == 0) {
        AnsiConsole.MarkupLine("[red]❌ Provide at least one --source or --push target[/]");
        valid = false;
    }

    return valid;
}

// --- Auth resolution ---

static (string? Token, bool IsApiKey) ResolveAuth(string url) {
    if (url.Contains("nuget.pkg.github.com"))
        return (Environment.GetEnvironmentVariable("GITHUB_TOKEN"), false);
    if (url.Contains("api.nuget.org"))
        return (Environment.GetEnvironmentVariable("NUGET_TOKEN"), true);
    if (url.Contains("pkgs.dev.azure.com"))
        return (Environment.GetEnvironmentVariable("AZURE_DEVOPS_TOKEN"), false);

    var fallback = Environment.GetEnvironmentVariable("NUWRAP_TOKEN");
    if (fallback is null)
        AnsiConsole.MarkupLine($"[yellow]⚠️  No token found for {url} — attempting unauthenticated[/]");
    return (fallback, false);
}

static SourceRepository BuildRepository(string url) {
    var (token, _) = ResolveAuth(url);
    var source = new PackageSource(url);
    if (token is not null)
        source.Credentials = new PackageSourceCredential(url, "nuwrap", token, true, null);
    return Repository.Factory.GetCoreV3(source);
}

// --- Source partitioning ---

static (List<SourceConfig> feeds, List<FileInfo> files) PartitionSources(NuWrapConfig config) {
    // If no sources provided, fall back to push targets as implicit sources in order
    var sources = config.Sources.Count > 0
        ? config.Sources
        : config.Push.Select(p => new SourceConfig(p.Url)).ToList();

    var feeds = sources.Where(s => !IsLocalPath(s.Url)).ToList();
    var files = new List<FileInfo>();
    foreach (var s in sources.Where(s => IsLocalPath(s.Url)))
        files.AddRange(ResolveLocalFiles(s.Url));
    return (feeds, files);
}

static List<FileInfo> ResolveLocalFiles(string path) {
    if (File.Exists(path) && path.EndsWith(".nupkg")) return [new FileInfo(path)];
    if (Directory.Exists(path)) return [.. new DirectoryInfo(path).GetFiles("*.nupkg")];
    AnsiConsole.MarkupLine($"[yellow]⚠️  Source not found: {path}[/]");
    return [];
}

static bool IsLocalPath(string url) =>
    !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

// --- Package resolution ---

record ResolvedPackage(string Id, NuGetVersion? PinnedVersion);

static ResolvedPackage ParsePackageEntry(string entry) {
    var parts = entry.Split('@', 2);
    if (parts.Length == 2 && NuGetVersion.TryParse(parts[1], out var pinned))
        return new(parts[0].Trim(), pinned);
    return new(entry.Trim(), null);
}

// --- NuGet operations ---

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

static async Task PushToFeed(string url, IEnumerable<FileInfo> packages) {
    var (token, isApiKey) = ResolveAuth(url);
    var repository = BuildRepository(url);
    var updateResource = await repository.GetResourceAsync<PackageUpdateResource>();

    foreach (var pkg in packages) {
        try {
            await updateResource.Push(
                [pkg.FullName], null, 30, false,
                _ => isApiKey ? token : null,
                _ => null, false, false, null, NullLogger.Instance);
            AnsiConsole.MarkupLine($"  [green]✅[/] {Markup.Escape(pkg.Name)}");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"  [red]❌ {Markup.Escape(pkg.Name)}: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

// --- Nuspec manipulation ---

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

// --- Stamping ---

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

        var packageId        = metadata.Element(ns + "id")!.Value;
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

// --- Promotion run ---

static async Task<int> RunPromotion(NuWrapConfig config, NuGetVersion targetVersion) {
    var (remoteFeeds, localFiles) = PartitionSources(config);
    var output = new DirectoryInfo(config.Output);
    output.Create();

    var allFiles = new List<FileInfo>(localFiles);

    // Download remote packages into temp location first
    var downloadTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(downloadTemp);

    foreach (var feed in remoteFeeds) {
        var repository = BuildRepository(feed.Url);
        foreach (var packageId in config.Packages) {
            var downloaded = await DownloadLatestPreRelease(packageId, repository, downloadTemp);
            if (downloaded is not null)
                allFiles.Add(new FileInfo(downloaded.Value.DownloadPath));
            else
                AnsiConsole.MarkupLine($"[red]❌ '{packageId}' not found on {feed.Url}[/]");
        }
    }

    var siblingIds = allFiles
        .Select(ResolvePackageId)
        .Where(id => id is not null)
        .Select(id => id!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var results = new List<StampResult>();

    await AnsiConsole.Progress()
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

    PrintSummary(results, output);

    if (results.All(r => r.Success) && config.Push.Count > 0) {
        var stamped = output.GetFiles("*.nupkg");
        foreach (var push in config.Push) {
            AnsiConsole.MarkupLine($"\n[mediumPurple]Pushing to[/] [grey]{push.Url}[/]");
            if (IsLocalPath(push.Url)) {
                var dir = Directory.CreateDirectory(push.Url);
                foreach (var pkg in stamped)
                    File.Copy(pkg.FullName, Path.Combine(dir.FullName, pkg.Name), overwrite: true);
            } else {
                await PushToFeed(push.Url, stamped);
            }
        }
    }

    return results.Any(r => !r.Success) ? 1 : 0;
}

// --- Dry run ---

static async Task<int> RunDryRun(NuWrapConfig config, NuGetVersion targetVersion) {
    var (remoteFeeds, localFiles) = PartitionSources(config);
    var results = new List<StampResult>();

    var resolvedPackages = config.Packages.Select(ParsePackageEntry).ToList();

    foreach (var package in resolvedPackages) {
        bool found = false;
        foreach (var feed in remoteFeeds) {
            try {
                var repository = BuildRepository(feed.Url);
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
            var packageId = metadata?.Element(ns + "id")?.Value ?? file.Name;
            var currentStr = metadata?.Element(ns + "version")?.Value ?? "unknown";
            NuGetVersion.TryParse(currentStr, out var current);
            var valid = config.Force || current is null || targetVersion > current;
            results.Add(new(packageId, currentStr, config.Version!, valid,
                valid ? null : $"'{targetVersion}' is not greater than '{currentStr}'"));
        } catch (Exception ex) {
            results.Add(new(file.Name, null, config.Version!, false, ex.Message));
        }
    }

    PrintDryRunPlan(results, config);
    return results.Any(r => !r.Success) ? 1 : 0;
}

static void PrintDryRunPlan(List<StampResult> results, NuWrapConfig config) {
    AnsiConsole.Write(new Rule("[mediumPurple]Promotion Plan[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var table = BuildResultsTable(results);
    AnsiConsole.Write(table);

    if (config.Push.Count > 0) {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Push targets:[/]");
        foreach (var push in config.Push)
            AnsiConsole.MarkupLine($"  [grey]→[/] {Markup.Escape(push.Url)}");
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine(results.All(r => r.Success)
        ? "[green]✅ All packages ready for promotion[/]"
        : "[red]❌ Some packages have issues — resolve before releasing[/]");
}

// --- Summary ---

static void PrintSummary(List<StampResult> results, DirectoryInfo output) {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(BuildResultsTable(results));
    AnsiConsole.WriteLine();

    var succeeded = results.Count(r => r.Success);
    var failed    = results.Count(r => !r.Success);

    if (failed == 0)
        AnsiConsole.MarkupLine($"[green]✅ {succeeded} package(s) stamped → {Markup.Escape(output.FullName)}[/]");
    else
        AnsiConsole.MarkupLine($"[yellow]⚠️  {succeeded} succeeded,[/] [red]{failed} failed[/]");
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
