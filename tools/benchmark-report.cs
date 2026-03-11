#!/usr/bin/env dotnet-run
#:package Spectre.Console@0.*
#:package Spectre.Console.Cli@0.*

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<ReportCommand>();
app.Configure(c => c.SetApplicationName("benchmark-report"));
return app.Run(args);

// ─── BenchmarkDotNet JSON Models ─────────────────────────────────────────────

record BdnRoot(
    string?              Title,
    BdnHostInfo?         HostEnvironmentInfo,
    List<BdnBenchmark>   Benchmarks
);

record BdnHostInfo(
    string? BenchmarkDotNetVersion,
    string? OsVersion,
    string? ProcessorName,
    int?    PhysicalProcessorCount,
    int?    PhysicalCoreCount,
    int?    LogicalCoreCount,
    string? RuntimeVersion,
    string? Architecture,
    string? Configuration
);

record BdnBenchmark(
    string?         Type,
    string?         Method,
    string?         Parameters,
    string?         DisplayInfo,
    BdnStatistics?  Statistics,
    BdnMemory?      Memory,
    List<BdnMeasurement>? Measurements
);

record BdnStatistics(
    double  Mean,
    double  Median,
    double  StandardDeviation,
    BdnConfidenceInterval? ConfidenceInterval,
    JsonElement? Percentiles
);

record BdnConfidenceInterval(
    double Margin
);

record BdnMemory(
    long BytesAllocatedPerOperation,
    int  Gen0Collections,
    int  Gen1Collections,
    int  Gen2Collections
);

record BdnMeasurement(
    string? IterationMode,
    string? IterationStage,
    double  Value
);

record JobConfig(
    string? Toolchain,
    int?    WarmupCount,
    int?    IterationCount
);

// ─── Report Models ───────────────────────────────────────────────────────────

record BenchmarkEntry(
    string  Type,
    string  Method,
    string? Parameters,
    string  CompositeKey,
    double  MeanNs,
    double  MedianNs,
    double  StdDevNs,
    double  ErrorNs,
    double  P95Ns,
    double  OpsPerSec,
    long    AllocatedBytes,
    int     Gen0,
    int     Gen1,
    int     Gen2,
    int     Rank
);

record ComparisonEntry(
    BenchmarkEntry Current,
    BenchmarkEntry? Baseline,
    double  DeltaPercent,
    string  Classification
);

record ReportData(
    BdnHostInfo?            Environment,
    JobConfig?              JobConfig,
    List<BenchmarkEntry>    Benchmarks,
    List<ComparisonEntry>?  Comparisons,
    bool                    IsComparison
);

record SummaryJson(
    string  GeneratedAt,
    bool    IsComparison,
    int     TotalBenchmarks,
    int     Regressions,
    int     Improvements,
    List<RegressionDetail> RegressionDetails
);

record RegressionDetail(
    string  Name,
    double  CurrentMeanNs,
    double  BaselineMeanNs,
    double  DeltaPercent
);

// ─── JSON Source Generation ──────────────────────────────────────────────────

[JsonSerializable(typeof(BdnRoot))]
[JsonSerializable(typeof(List<BenchmarkEntry>))]
[JsonSerializable(typeof(List<ComparisonEntry>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(BdnHostInfo))]
[JsonSerializable(typeof(JobConfig))]
[JsonSerializable(typeof(SummaryJson))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
partial class ReportJsonContext : JsonSerializerContext;

// ─── Settings ────────────────────────────────────────────────────────────────

sealed class ReportSettings : CommandSettings {
    [CommandOption("--input")]
    [Description("Directory containing BenchmarkDotNet JSON results")]
    public string Input { get; init; } = "./artifacts/benchmarks";

    [CommandOption("--baseline")]
    [Description("Baseline directory for comparison mode")]
    public string? Baseline { get; init; }

    [CommandOption("--output")]
    [Description("Output HTML report path")]
    public string Output { get; init; } = "./benchmark-report.html";

    [CommandOption("--threshold")]
    [Description("Regression threshold percentage (default: 10.0)")]
    public double Threshold { get; init; } = 10.0;

    [CommandOption("--summary-json")]
    [Description("Machine-readable summary output path")]
    public string? SummaryJson { get; init; }
}

// ─── Command ─────────────────────────────────────────────────────────────────

sealed class ReportCommand : Command<ReportSettings> {
    static readonly ReportJsonContext Ctx = new(new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true
    });

    public override int Execute(CommandContext context, ReportSettings settings, CancellationToken cancellation = default) {
        AnsiConsole.Write(new FigletText("benchmark-report").Color(Color.MediumPurple));
        AnsiConsole.MarkupLine("[grey]BenchmarkDotNet JSON → HTML dashboard[/]\n");

        // ── Discover input files ────────────────────────────────────────────
        var inputFiles = DiscoverJsonFiles(settings.Input);
        if (inputFiles.Count == 0) {
            AnsiConsole.MarkupLine($"[red]No *-report-full*.json files found in {Markup.Escape(settings.Input)}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[grey]Input files:[/] [white]{inputFiles.Count}[/]");

        // ── Parse current results ───────────────────────────────────────────
        var (benchmarks, hostInfo, jobConfig) = ParseBenchmarkFiles(inputFiles);
        if (benchmarks.Count == 0) {
            AnsiConsole.MarkupLine("[red]No benchmarks found in input files[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[grey]Benchmarks:[/] [white]{benchmarks.Count}[/]");

        // ── Parse baseline (if comparison mode) ─────────────────────────────
        List<BenchmarkEntry>? baselineBenchmarks = null;
        List<ComparisonEntry>? comparisons = null;
        var isComparison = settings.Baseline is not null;

        if (isComparison) {
            var baselineFiles = DiscoverJsonFiles(settings.Baseline!);
            if (baselineFiles.Count == 0) {
                AnsiConsole.MarkupLine($"[red]No *-report-full*.json files found in baseline {Markup.Escape(settings.Baseline!)}[/]");
                return 1;
            }

            (baselineBenchmarks, _, _) = ParseBenchmarkFiles(baselineFiles);
            AnsiConsole.MarkupLine($"[grey]Baseline benchmarks:[/] [white]{baselineBenchmarks.Count}[/]");

            comparisons = BuildComparisons(benchmarks, baselineBenchmarks, settings.Threshold);
        }

        var reportData = new ReportData(hostInfo, jobConfig, benchmarks, comparisons, isComparison);

        // ── Generate HTML ───────────────────────────────────────────────────
        var html = GenerateHtml(reportData, settings.Threshold);
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(settings.Output));
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);
        File.WriteAllText(settings.Output, html);

        // ── Generate summary JSON ───────────────────────────────────────────
        var regressionCount  = comparisons?.Count(c => c.Classification == "Regressed") ?? 0;
        var improvementCount = comparisons?.Count(c => c.Classification == "Improved") ?? 0;

        if (settings.SummaryJson is not null) {
            var summary = new SummaryJson(
                GeneratedAt:      DateTime.UtcNow.ToString("o"),
                IsComparison:     isComparison,
                TotalBenchmarks:  benchmarks.Count,
                Regressions:      regressionCount,
                Improvements:     improvementCount,
                RegressionDetails: comparisons?
                    .Where(c => c.Classification == "Regressed")
                    .Select(c => new RegressionDetail(
                        c.Current.CompositeKey,
                        c.Current.MeanNs,
                        c.Baseline?.MeanNs ?? 0,
                        c.DeltaPercent))
                    .ToList() ?? []
            );

            var summaryDir = Path.GetDirectoryName(Path.GetFullPath(settings.SummaryJson));
            if (summaryDir is not null && !Directory.Exists(summaryDir))
                Directory.CreateDirectory(summaryDir);
            File.WriteAllText(settings.SummaryJson,
                JsonSerializer.Serialize(summary, new ReportJsonContext(new JsonSerializerOptions {
                    WriteIndented = true
                }).SummaryJson));
        }

        // ── Console summary ─────────────────────────────────────────────────
        PrintSummary(settings, benchmarks.Count, regressionCount, improvementCount, isComparison);

        // ── Exit code ───────────────────────────────────────────────────────
        if (isComparison && regressionCount > 0) {
            AnsiConsole.MarkupLine($"\n[red]{regressionCount} regression(s) detected above {settings.Threshold}% threshold[/]");
            return 1;
        }

        return 0;
    }

    // ─── Input discovery ─────────────────────────────────────────────────────

    static List<string> DiscoverJsonFiles(string directory) {
        if (!Directory.Exists(directory)) return [];

        return Directory.EnumerateFiles(directory, "*-report-full*.json", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
    }

    // ─── JSON parsing ────────────────────────────────────────────────────────

    static (List<BenchmarkEntry> Benchmarks, BdnHostInfo? HostInfo, JobConfig? JobConfig) ParseBenchmarkFiles(List<string> files) {
        var benchmarks = new List<BenchmarkEntry>();
        var rawBenchmarks = new List<BdnBenchmark>();
        BdnHostInfo? hostInfo = null;

        foreach (var file in files) {
            var json = File.ReadAllText(file);
            var root = JsonSerializer.Deserialize(json, Ctx.BdnRoot);
            if (root is null) continue;

            hostInfo ??= root.HostEnvironmentInfo;

            foreach (var b in root.Benchmarks) {
                rawBenchmarks.Add(b);

                var type   = b.Type ?? "Unknown";
                var method = b.Method ?? "Unknown";
                var key    = b.Parameters is not null
                    ? $"{type}.{method}|{b.Parameters}"
                    : $"{type}.{method}";

                var p95 = GetP95(b.Statistics?.Percentiles);

                benchmarks.Add(new BenchmarkEntry(
                    Type:           type,
                    Method:         method,
                    Parameters:     b.Parameters,
                    CompositeKey:   key,
                    MeanNs:         b.Statistics?.Mean ?? 0,
                    MedianNs:       b.Statistics?.Median ?? 0,
                    StdDevNs:       b.Statistics?.StandardDeviation ?? 0,
                    ErrorNs:        b.Statistics?.ConfidenceInterval?.Margin ?? 0,
                    P95Ns:          p95,
                    OpsPerSec:      b.Statistics?.Mean > 0 ? 1e9 / b.Statistics.Mean : 0,
                    AllocatedBytes: b.Memory?.BytesAllocatedPerOperation ?? 0,
                    Gen0:           b.Memory?.Gen0Collections ?? 0,
                    Gen1:           b.Memory?.Gen1Collections ?? 0,
                    Gen2:           b.Memory?.Gen2Collections ?? 0,
                    Rank:           0
                ));
            }
        }

        // Deduplicate by CompositeKey (last file wins)
        benchmarks = benchmarks
            .GroupBy(b => b.CompositeKey)
            .Select(g => g.Last())
            .ToList();

        // Compute ranks per class
        benchmarks = benchmarks
            .GroupBy(b => b.Type)
            .SelectMany(g => {
                var sorted = g.OrderBy(b => b.MeanNs).ToList();
                return sorted.Select((b, i) => b with { Rank = i + 1 });
            })
            .ToList();

        var jobConfig = ExtractJobConfig(rawBenchmarks);

        return (benchmarks, hostInfo, jobConfig);
    }

    static JobConfig? ExtractJobConfig(List<BdnBenchmark> benchmarks) {
        var displayInfo = benchmarks.FirstOrDefault(b => b.DisplayInfo is not null)?.DisplayInfo;
        if (displayInfo is null) return null;

        var toolchain      = Regex.Match(displayInfo, @"Toolchain=([^,\)]+)").Groups[1].Value.Trim();
        var warmupMatch    = Regex.Match(displayInfo, @"WarmupCount=(\d+)");
        var iterationMatch = Regex.Match(displayInfo, @"IterationCount=(\d+)");

        return new JobConfig(
            Toolchain:      string.IsNullOrEmpty(toolchain) ? null : toolchain,
            WarmupCount:    warmupMatch.Success   ? int.Parse(warmupMatch.Groups[1].Value)    : null,
            IterationCount: iterationMatch.Success ? int.Parse(iterationMatch.Groups[1].Value) : null
        );
    }

    static double GetP95(JsonElement? percentiles) {
        if (percentiles is null) return 0;

        var el = percentiles.Value;

        // Object format: { "P0": ..., "P95": ..., ... }
        if (el.ValueKind == JsonValueKind.Object) {
            if (el.TryGetProperty("P95", out var p95Prop))
                return p95Prop.GetDouble();
            // Case insensitive fallback
            foreach (var prop in el.EnumerateObject()) {
                if (prop.Name.Equals("P95", StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetDouble();
            }
        }

        // Array format: positional [P0, P25, P50, P67, P80, P85, P90, P95, P100]
        if (el.ValueKind == JsonValueKind.Array) {
            var arr = el.EnumerateArray().ToList();
            if (arr.Count >= 8) return arr[7].GetDouble();
        }

        return 0;
    }

    // ─── Comparison ──────────────────────────────────────────────────────────

    static List<ComparisonEntry> BuildComparisons(
        List<BenchmarkEntry> current,
        List<BenchmarkEntry> baseline,
        double threshold)
    {
        var baselineMap = baseline.ToDictionary(b => b.CompositeKey);

        return current.Select(c => {
            baselineMap.TryGetValue(c.CompositeKey, out var b);
            var delta = b is not null && b.MeanNs > 0
                ? ((c.MeanNs - b.MeanNs) / b.MeanNs) * 100.0
                : 0.0;

            var classification = b is null
                ? "New"
                : Math.Abs(delta) <= threshold
                    ? "Neutral"
                    : delta > 0
                        ? "Regressed"
                        : "Improved";

            return new ComparisonEntry(c, b, delta, classification);
        }).ToList();
    }

    // ─── Console output ──────────────────────────────────────────────────────

    static void PrintSummary(
        ReportSettings settings,
        int totalBenchmarks,
        int regressions,
        int improvements,
        bool isComparison)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("[grey]Mode[/]",        isComparison ? "[yellow]comparison[/]" : "[white]single-run[/]");
        grid.AddRow("[grey]Benchmarks[/]",  $"[white]{totalBenchmarks}[/]");

        if (isComparison) {
            grid.AddRow("[grey]Regressions[/]",  regressions > 0
                ? $"[red]{regressions}[/]"
                : $"[green]{regressions}[/]");
            grid.AddRow("[grey]Improvements[/]", improvements > 0
                ? $"[green]{improvements}[/]"
                : $"[white]{improvements}[/]");
            grid.AddRow("[grey]Threshold[/]",    $"[white]{settings.Threshold}%[/]");
        }

        grid.AddRow("[grey]Output[/]", $"[white]{settings.Output}[/]");
        if (settings.SummaryJson is not null)
            grid.AddRow("[grey]Summary JSON[/]", $"[white]{settings.SummaryJson}[/]");

        AnsiConsole.Write(new Panel(grid)
            .Header("[mediumPurple] Report Summary [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.MediumPurple));
    }

    // ─── HTML generation ─────────────────────────────────────────────────────

    static string GenerateHtml(ReportData data, double threshold) {
        var sb = new StringBuilder();

        // Serialize data as JSON for client-side rendering
        var benchmarkJson = JsonSerializer.Serialize(data.Benchmarks, Ctx.ListBenchmarkEntry);
        var comparisonJson = data.Comparisons is not null
            ? JsonSerializer.Serialize(data.Comparisons, Ctx.ListComparisonEntry)
            : "null";
        var envJson = data.Environment is not null
            ? JsonSerializer.Serialize(data.Environment, Ctx.BdnHostInfo)
            : "null";
        var jobConfigJson = data.JobConfig is not null
            ? JsonSerializer.Serialize(data.JobConfig, Ctx.JobConfig)
            : "null";

        var classes = data.Benchmarks
            .Select(b => b.Type)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var classesJson = JsonSerializer.Serialize(classes, Ctx.ListString);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en" data-theme="dark">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Kurrent.Client Benchmark Report</title>
            <link href="https://cdn.jsdelivr.net/npm/daisyui@5" rel="stylesheet" type="text/css" />
            <link href="https://cdn.jsdelivr.net/npm/daisyui@5/themes.css" rel="stylesheet" type="text/css" />
            <script src="https://cdn.jsdelivr.net/npm/@tailwindcss/browser@4"></script>
            <script src="https://unpkg.com/lucide@latest/dist/umd/lucide.min.js"></script>
            <script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>
            <style>
              html { scroll-behavior: smooth; }
              .collapsed-body { display: none; }
              .collapsed-toggle { transform: rotate(-90deg); }
              th[data-sort] { cursor: pointer; user-select: none; white-space: nowrap; }
              th[data-sort] .col-icon { display: inline-block; width: 12px; height: 12px; vertical-align: middle; margin-right: 2px; opacity: 0.5; }
              th[data-sort] .sort-icon { display: inline-block; width: 14px; height: 14px; vertical-align: middle; margin-left: 2px; opacity: 0.3; }
              th[data-sort].sort-asc .sort-icon, th[data-sort].sort-desc .sort-icon { opacity: 1; }
              td[data-heatmap] { transition: background-color 0.15s ease, color 0.15s ease; }
              .heatmap-text td[data-heatmap] { background-color: transparent !important; }
              tr.best-row td { font-weight: 700; }
              table.col-hover col.hover { background: oklch(0.5 0 0 / 0.04); }
              .sticky-header { position: sticky; top: 0; z-index: 50; background-color: oklch(var(--b1)); backdrop-filter: blur(8px); box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
              .fab-top { position: fixed; bottom: 2rem; right: 2rem; z-index: 50; opacity: 0; pointer-events: none; transition: opacity 0.2s ease; }
              .fab-top.visible { opacity: 1; pointer-events: auto; }
            </style>
            </head>
            <body class="bg-base-100 text-base-content leading-relaxed max-w-[1400px] mx-auto">

            <div class="sticky-header px-8 pt-8 pb-4 border-b border-base-300 mb-6">
              <div class="flex justify-between items-center mb-3">
                <div class="flex items-center gap-3">
                  <h1 class="text-xl font-semibold">Kurrent.Client Benchmark Report</h1>
                  <span class="badge badge-soft {{(data.IsComparison ? "badge-warning" : "badge-info")}}">
                    {{(data.IsComparison ? "comparison" : "single-run")}}
                  </span>
                  <span class="badge badge-soft badge-ghost" id="benchmark-count-badge"></span>
                  <span class="text-base-content/40 text-xs" id="generated-ts">Generated {{timestamp}}</span>
                </div>
                <div class="flex items-center gap-2">
                  <button class="btn btn-xs btn-ghost btn-square" id="toggle-all" title="Expand/Collapse all">
                    <i data-lucide="unfold-vertical" class="w-4 h-4" id="toggle-all-icon"></i>
                  </button>
                  <details class="dropdown dropdown-end">
                    <summary class="btn btn-xs btn-ghost btn-square" title="Settings">
                      <i data-lucide="settings" class="w-4 h-4"></i>
                    </summary>
                    <div class="dropdown-content bg-base-200 rounded-box z-50 w-64 shadow-lg border border-base-300 mt-2 p-3 flex flex-col gap-3">
                      <div class="flex items-center justify-between">
                        <span class="text-xs font-semibold">Text size</span>
                        <div class="flex items-center gap-1">
                          <button class="btn btn-xs btn-ghost btn-square" id="zoom-out"><i data-lucide="minus" class="w-3 h-3"></i></button>
                          <span class="text-xs font-mono text-base-content/50 w-8 text-center" id="zoom-label">16</span>
                          <button class="btn btn-xs btn-ghost btn-square" id="zoom-in"><i data-lucide="plus" class="w-3 h-3"></i></button>
                        </div>
                      </div>
                      <div class="flex items-center justify-between">
                        <span class="text-xs font-semibold">Heatmap</span>
                        <label class="flex items-center gap-1.5 cursor-pointer">
                          <span class="text-base-content/50 text-xs font-mono" id="heatmap-label">cells</span>
                          <input type="checkbox" class="toggle toggle-xs" id="heatmap-toggle" />
                        </label>
                      </div>
                      <div class="flex items-center justify-between">
                        <span class="text-xs font-semibold">Theme</span>
                        <select class="select select-xs select-ghost w-auto font-mono" id="theme-select">
                          <optgroup label="Dark">
                            <option value="dark">dark</option>
                            <option value="night">night</option>
                            <option value="abyss">abyss</option>
                            <option value="dim">dim</option>
                            <option value="sunset">sunset</option>
                            <option value="dracula">dracula</option>
                            <option value="business">business</option>
                            <option value="synthwave">synthwave</option>
                            <option value="cyberpunk">cyberpunk</option>
                            <option value="halloween">halloween</option>
                            <option value="forest">forest</option>
                            <option value="aqua">aqua</option>
                            <option value="black">black</option>
                            <option value="luxury">luxury</option>
                            <option value="coffee">coffee</option>
                            <option value="nord">nord</option>
                          </optgroup>
                          <optgroup label="Light">
                            <option value="light">light</option>
                            <option value="corporate">corporate</option>
                            <option value="winter">winter</option>
                            <option value="caramellatte">caramellatte</option>
                            <option value="silk">silk</option>
                            <option value="cupcake">cupcake</option>
                            <option value="bumblebee">bumblebee</option>
                            <option value="emerald">emerald</option>
                            <option value="retro">retro</option>
                            <option value="valentine">valentine</option>
                            <option value="garden">garden</option>
                            <option value="lofi">lofi</option>
                            <option value="pastel">pastel</option>
                            <option value="fantasy">fantasy</option>
                            <option value="wireframe">wireframe</option>
                            <option value="cmyk">cmyk</option>
                            <option value="autumn">autumn</option>
                            <option value="acid">acid</option>
                            <option value="lemonade">lemonade</option>
                          </optgroup>
                        </select>
                      </div>
                    </div>
                  </details>
                </div>
              </div>
              <div class="flex items-center gap-2">
                <input class="input input-sm input-bordered flex-1" id="global-search" type="search"
                       placeholder="Search benchmarks..." autocomplete="off">
                <select class="select select-sm select-bordered w-auto font-mono" id="section-nav">
                  <option value="">Jump to...</option>
                </select>
              </div>
            </div>

            <div class="px-8">

            <div id="env-section"></div>
            <div id="stats-row" class="hidden"></div>
            <div id="regression-section"></div>
            <div id="class-sections"></div>

            <div class="bg-base-200 border border-base-300 rounded-lg mb-6 px-4 py-3">
              <div class="text-xs font-semibold mb-2 flex items-center gap-2">
                <i data-lucide="book-open" class="w-3.5 h-3.5"></i> Legends
              </div>
                <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-xs font-mono">
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="bar-chart-2" class="w-3 h-3"></i> Mean</dt>
                  <dd>Arithmetic mean of all measurements</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="shield-alert" class="w-3 h-3"></i> Error</dt>
                  <dd>Half of 99.9% confidence interval</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="activity" class="w-3 h-3"></i> StdDev</dt>
                  <dd>Standard deviation of all measurements</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="percent" class="w-3 h-3"></i> P95</dt>
                  <dd>95th percentile of all measurements</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="trophy" class="w-3 h-3"></i> Rank</dt>
                  <dd>Relative position of the benchmark within its class (lower is better)</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="zap" class="w-3 h-3"></i> Ops/sec</dt>
                  <dd>Operations per second (1s / Mean)</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="recycle" class="w-3 h-3"></i> Gen0</dt>
                  <dd>GC Generation 0 collects per 1000 operations</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="recycle" class="w-3 h-3"></i> Gen1</dt>
                  <dd>GC Generation 1 collects per 1000 operations</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="recycle" class="w-3 h-3"></i> Gen2</dt>
                  <dd>GC Generation 2 collects per 1000 operations</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="memory-stick" class="w-3 h-3"></i> Allocated</dt>
                  <dd>Allocated memory per single operation (managed only, inclusive, 1 KB = 1024 B)</dd>
                  <dt class="col-span-2 border-t border-base-300 mt-1 pt-2 text-base-content/40 font-semibold text-[10px] uppercase tracking-wider">Time Units</dt>
                  <dd class="hidden"></dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="clock" class="w-3 h-3"></i> 1 ns</dt>
                  <dd>1 Nanosecond (0.000000001 sec)</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="clock" class="w-3 h-3"></i> 1 &micro;s</dt>
                  <dd>1 Microsecond (0.000001 sec)</dd>
                  <dt class="text-base-content/60 font-semibold flex items-center gap-1.5"><i data-lucide="clock" class="w-3 h-3"></i> 1 ms</dt>
                  <dd>1 Millisecond (0.001 sec)</dd>
                </dl>
            </div>

            <footer class="text-center text-base-content/40 text-xs pt-4 border-t border-base-300">
              <code class="font-mono">Kurrent.Client</code> &middot; Generated by benchmark-report
            </footer>
            </div><!-- /px-8 -->

            <button class="fab-top btn btn-circle btn-sm btn-primary shadow-lg" id="fab-top" title="Back to top">
              <i data-lucide="arrow-up" class="w-4 h-4"></i>
            </button>

            <script>
            const benchmarks = {{benchmarkJson}};
            const comparisons = {{comparisonJson}};
            const env = {{envJson}};
            const jobConfig = {{jobConfigJson}};
            const classes = {{classesJson}};
            const isComparison = {{(data.IsComparison ? "true" : "false")}};
            const threshold = {{threshold}};

            // ── Formatting ──────────────────────────────────────────────────────

            function formatNs(ns) {
              if (ns >= 1_000_000) return (ns / 1_000_000).toFixed(2) + " ms";
              if (ns >= 1_000) return (ns / 1_000).toFixed(2) + " \u00B5s";
              return ns.toFixed(1) + " ns";
            }

            function formatBytes(bytes) {
              if (bytes === 0) return "0 B";
              if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + " MB";
              if (bytes >= 1024) return (bytes / 1024).toFixed(1) + " KB";
              return bytes + " B";
            }

            function formatDelta(pct) {
              const sign = pct > 0 ? "+" : "";
              return sign + pct.toFixed(1) + "%";
            }

            function deltaClass(classification) {
              switch (classification) {
                case "Regressed": return "badge-error";
                case "Improved":  return "badge-success";
                case "New":       return "badge-secondary";
                default:          return "badge-ghost";
              }
            }

            function rankBadge(rank) {
              const cls = { 1: "badge-success", 2: "badge-info", 3: "badge-warning" }[rank] || "";
              return `<span class="badge badge-xs ${cls ? 'badge-soft ' + cls : ''}">${rank}</span>`;
            }

            function heatmapRatio(value, min, max) {
              if (min === max) return -1;
              return (max - value) / (max - min);  // 1.0 = best (lowest), 0.0 = worst (highest)
            }

            function heatmapColorFromRatio(ratio, lighten) {
              if (ratio < 0) return "";
              // HSL-based: ratio 0 = worst (red), 0.5 = neutral (amber), 1 = best (green)
              // Hue: 0 (red) → 45 (amber) → 142 (green)
              let hue;
              if (ratio <= 0.5) {
                hue = ratio * 2 * 45;       // 0→45
              } else {
                hue = 45 + (ratio - 0.5) * 2 * 97;  // 45→142
              }
              const sat = 55;  // low saturation for soft pastels
              const light = 40 + lighten * 45;  // push toward lighter
              return `hsl(${hue}, ${sat}%, ${light}%)`;
            }

            function allocBadge(bytes) {
              const cls = bytes === 0 ? "badge-ghost" : "badge-warning";
              return `<span class="badge badge-xs badge-soft ${cls}">${formatBytes(bytes)}</span>`;
            }

            function benchLabel(b) {
              return b.Parameters ? `${b.Method}(${b.Parameters})` : b.Method;
            }

            function statCardDetails(bench) {
              const paramPairs = bench.Parameters ? bench.Parameters.split("&").map(p => {
                const [k, ...v] = p.split("=");
                return [k, v.join("=")];
              }) : [];
              const paramBadges = paramPairs.map(([k, v]) =>
                `<span class="badge badge-soft badge-sm font-mono gap-1"><i data-lucide="settings-2" class="w-3 h-3 opacity-40"></i><span class="opacity-50">${k}=</span>${v}</span>`
              ).join("");
              return `
                <div class="border-t border-base-content/10 mt-2.5 pt-2.5">
                  <div class="text-sm font-semibold flex items-center gap-1.5"><i data-lucide="flask-conical" class="w-3.5 h-3.5 opacity-60"></i>${bench.Method}</div>
                  ${paramBadges ? `<div class="flex flex-wrap gap-1.5 mt-2">${paramBadges}</div>` : ""}
                </div>`;
            }

            function shortLabel(b) {
              if (b.Parameters) {
                return b.Parameters.includes("=") ? b.Parameters.split("=").slice(1).join("=") : b.Parameters;
              }
              return b.Method;
            }

            function formatOps(ops) {
              if (ops >= 1_000_000_000) return (ops / 1_000_000_000).toFixed(2) + " Gops/s";
              if (ops >= 1_000_000)     return (ops / 1_000_000).toFixed(2)     + " Mops/s";
              if (ops >= 1_000)         return (ops / 1_000).toFixed(2)         + " Kops/s";
              return ops.toFixed(0) + " ops/s";
            }

            function formatGen(val) {
              return val === 0 ? `<span class="text-base-content/40">\u2014</span>` : String(val);
            }

            // ── Color palette for bars ──────────────────────────────────────────

            function barColor(index, total) {
              if (total <= 1) return "hsla(142, 65%, 50%, 1)";
              const hue = 142 - (index / (total - 1)) * 142;
              return `hsla(${hue}, 65%, 50%, 1)`;
            }

            function barColorAlpha(index, total, alpha) {
              if (total <= 1) return `hsla(142, 65%, 50%, ${alpha})`;
              const hue = 142 - (index / (total - 1)) * 142;
              return `hsla(${hue}, 65%, 50%, ${alpha})`;
            }

            // ── Metric definitions ──────────────────────────────────────────────

            const metrics = [
              { key: "MeanNs",         label: "Mean",      format: formatNs },
              { key: "OpsPerSec",      label: "Ops/sec",   format: formatOps },
              { key: "ErrorNs",        label: "Error",     format: formatNs },
              { key: "StdDevNs",       label: "StdDev",    format: formatNs },
              { key: "P95Ns",          label: "P95",       format: formatNs },
              { key: "AllocatedBytes", label: "Allocated", format: formatBytes },
              { key: "Gen0",           label: "Gen0",      format: v => String(v) },
              { key: "Gen1",           label: "Gen1",      format: v => String(v) },
              { key: "Gen2",           label: "Gen2",      format: v => String(v) },
            ];

            // ── Environment section ─────────────────────────────────────────────

            if (env) {
              const machine = [];
              const config = [];

              if (env.OsVersion) machine.push({ icon: "monitor", label: "OS", value: env.OsVersion });
              if (env.ProcessorName) machine.push({ icon: "cpu", label: "CPU", value: env.ProcessorName });
              if (env.LogicalCoreCount) machine.push({
                icon: "grid-2x2", label: "Cores",
                value: `${env.PhysicalCoreCount || "?"} physical, ${env.LogicalCoreCount} logical`
              });
              if (env.Architecture) machine.push({ icon: "binary", label: "Architecture", value: env.Architecture });

              if (env.RuntimeVersion) config.push({ icon: "terminal", label: "Runtime", value: env.RuntimeVersion });
              if (env.BenchmarkDotNetVersion) config.push({ icon: "gauge", label: "BenchmarkDotNet", value: env.BenchmarkDotNetVersion });
              if (jobConfig) {
                if (jobConfig.Toolchain) config.push({ icon: "wrench", label: "Toolchain", value: jobConfig.Toolchain });
                if (jobConfig.WarmupCount != null) config.push({ icon: "flame", label: "Warmup", value: jobConfig.WarmupCount + (jobConfig.WarmupCount === 1 ? " iteration" : " iterations") });
                if (jobConfig.IterationCount != null) config.push({ icon: "repeat", label: "Iterations", value: jobConfig.IterationCount + (jobConfig.IterationCount === 1 ? " iteration" : " iterations") });
              }
              if (env.Configuration) config.push({ icon: "settings", label: "Configuration", value: env.Configuration });

              function metaColumn(items) {
                return `
                  <div class="grid grid-cols-[1rem_auto_1fr] gap-x-3 gap-y-2 text-sm items-center">
                    ${items.map(i => `
                      <span class="text-base-content/40"><i data-lucide="${i.icon}" class="w-4 h-4"></i></span>
                      <span class="font-medium text-base-content/50">${i.label}</span>
                      <span class="font-mono text-base-content/80">${i.value}</span>
                    `).join("")}
                  </div>
                `;
              }

              document.getElementById("env-section").innerHTML = `
                <div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
                  ${metaColumn(machine)}
                  ${metaColumn(config)}
                </div>
              `;
            }

            // ── Header benchmark count ─────────────────────────────────────────

            {
              const badge = document.getElementById("benchmark-count-badge");
              if (badge) badge.textContent = `${benchmarks.length} benchmarks`;
            }

            // ── Section navigation (in sticky header) ──────────────────────────

            {
              const nav = document.getElementById("section-nav");
              classes.forEach(c => {
                const shortName = c.split(".").pop();
                const opt = document.createElement("option");
                opt.value = "section-" + c;
                opt.textContent = shortName;
                nav.appendChild(opt);
              });
              if (classes.length <= 1) nav.classList.add("hidden");
              nav.addEventListener("change", () => {
                if (!nav.value) return;
                // Collapse all sections
                document.querySelectorAll(".class-section").forEach(s => {
                  const body = document.getElementById("body-" + s.id);
                  const toggle = document.getElementById("toggle-" + s.id);
                  if (body) body.classList.add("collapsed-body");
                  if (toggle) toggle.classList.add("collapsed-toggle");
                });
                // Expand the selected one
                const el = document.getElementById(nav.value);
                if (el) {
                  const body = document.getElementById("body-" + nav.value);
                  const toggle = document.getElementById("toggle-" + nav.value);
                  if (body) body.classList.remove("collapsed-body");
                  if (toggle) toggle.classList.remove("collapsed-toggle");
                  el.scrollIntoView({ behavior: "smooth", block: "start" });
                }
                nav.value = "";
              });
            }

            // ── Global search (in sticky header) ─────────────────────────────────

            document.getElementById("global-search").addEventListener("input", e => {
              const term = e.target.value.toLowerCase().trim();

              document.querySelectorAll(".class-section").forEach(section => {
                let sectionVisible = false;

                section.querySelectorAll("tr[data-params]").forEach(el => {
                  const text = (el.querySelector("td:first-child")?.textContent ?? "").toLowerCase();
                  const match = !term || text.includes(term);
                  el.style.display = match ? "" : "none";
                  if (match) sectionVisible = true;
                });

                section.style.display = (!term || sectionVisible) ? "" : "none";
              });
            });

            // ── Regression summary (comparison mode) ────────────────────────────

            if (isComparison && comparisons) {
              const regressions = comparisons.filter(c => c.Classification === "Regressed");
              if (regressions.length > 0) {
                const rows = regressions
                  .sort((a, b) => b.DeltaPercent - a.DeltaPercent)
                  .map(c => `
                    <tr>
                      <td class="font-mono">${benchLabel(c.Current)}</td>
                      <td class="text-right font-mono">${formatNs(c.Baseline?.MeanNs ?? 0)}</td>
                      <td class="text-right font-mono">${formatNs(c.Current.MeanNs)}</td>
                      <td class="text-right font-mono"><span class="badge badge-xs badge-soft badge-error">${formatDelta(c.DeltaPercent)}</span></td>
                    </tr>
                  `).join("");

                document.getElementById("regression-section").innerHTML = `
                  <div class="rounded-lg border border-error/50 overflow-hidden mb-6">
                    <h2 class="text-base font-semibold px-5 pt-4 pb-3 text-error">Regressions (>${threshold}% threshold)</h2>
                    <table class="table table-sm">
                      <thead>
                        <tr>
                          <th>Benchmark</th>
                          <th class="text-right">Baseline</th>
                          <th class="text-right">Current</th>
                          <th class="text-right">Delta</th>
                        </tr>
                      </thead>
                      <tbody>${rows}</tbody>
                    </table>
                  </div>
                `;
              }
            }

            // ── Per-class sections ──────────────────────────────────────────────

            const classSections = document.getElementById("class-sections");

            classes.forEach((cls, classIndex) => {
              const classBenchmarks = benchmarks
                .filter(b => b.Type === cls)
                .sort((a, b) => a.MeanNs - b.MeanNs);

              if (classBenchmarks.length === 0) return;

              const shortName = cls.split(".").pop();
              const sectionId = `section-${cls}`;
              const maxMean = Math.max(...classBenchmarks.map(b => b.MeanNs));

              // Build comparison lookup
              const compMap = {};
              if (isComparison && comparisons) {
                comparisons.forEach(c => { compMap[c.Current.CompositeKey] = c; });
              }

              // Find max for bar scaling (include baseline if present)
              let barMax = maxMean;
              if (isComparison) {
                classBenchmarks.forEach(b => {
                  const comp = compMap[b.CompositeKey];
                  if (comp?.Baseline) barMax = Math.max(barMax, comp.Baseline.MeanNs);
                });
              }

              // Collect unique parameters for filters
              const params = [...new Set(classBenchmarks.map(b => b.Parameters).filter(Boolean))];

              // Every parameter key becomes its own column
              const allParamKeys = getParamKeys(classBenchmarks);
              const extraParamKeys = allParamKeys;

              const allZeroAlloc = classBenchmarks.every(b => b.AllocatedBytes === 0);
              const allZeroGen0 = classBenchmarks.every(b => b.Gen0 === 0);
              const allZeroGen1 = classBenchmarks.every(b => b.Gen1 === 0);
              const allZeroGen2 = classBenchmarks.every(b => b.Gen2 === 0);

              // Chart.js data placeholder (built after DOM insert)

              // Compute per-column min/max for heatmap
              const heatCols = ["MeanNs", "ErrorNs", "StdDevNs", "P95Ns"];
              if (!allZeroAlloc) heatCols.push("AllocatedBytes");
              heatCols.push("OpsPerSec");
              // Columns where higher = better (heatmap ratio inverted)
              const invertedHeatCols = new Set(["OpsPerSec"]);
              const heatRange = {};
              heatCols.forEach(k => {
                const vals = classBenchmarks.map(b => b[k] ?? 0).filter(v => v > 0);
                heatRange[k] = { min: Math.min(...vals), max: Math.min(Math.max(...vals), Math.min(...vals) * 3) };
              });

              const fastestBench = classBenchmarks.reduce((a, b) => a.MeanNs <= b.MeanNs ? a : b);
              const slowestBench = classBenchmarks.reduce((a, b) => a.MeanNs >= b.MeanNs ? a : b);
              const heaviestBench = classBenchmarks.reduce((a, b) => a.AllocatedBytes >= b.AllocatedBytes ? a : b);
              const bestMeanNs = fastestBench.MeanNs;
              const worstMeanNs = slowestBench.MeanNs;
              const maxOps = Math.max(...classBenchmarks.map(b => b.OpsPerSec));

              // Table rows
              const tableRows = classBenchmarks.map(b => {
                let deltaCell = "";
                if (isComparison) {
                  const comp = compMap[b.CompositeKey];
                  if (comp) {
                    const cls2 = deltaClass(comp.Classification);
                    const label = comp.Classification === "New" ? "new" : formatDelta(comp.DeltaPercent);
                    deltaCell = `<td class="text-right font-mono"><span class="badge badge-xs badge-soft ${cls2}">${label}</span></td>`;
                  } else {
                    deltaCell = `<td class="text-right font-mono">\u2014</td>`;
                  }
                }

                const isBest = b.MeanNs === bestMeanNs;
                const hm = (key) => {
                  const r = heatRange[key];
                  if (!r) return "";
                  const v = b[key] ?? 0;
                  if (v === 0) return "";
                  const clamped = Math.min(v, r.max);
                  let ratio = heatmapRatio(clamped, r.min, r.max);
                  if (invertedHeatCols.has(key)) ratio = 1.0 - ratio;
                  return ratio >= 0 ? ` data-heatmap data-heat-ratio="${ratio.toFixed(4)}"` : "";
                };

                const bParams = parseParams(b.Parameters);
                const benchName = b.Method;
                const extraParamCells = extraParamKeys.map(k => {
                  const v = bParams[k] || "\u2014";
                  return `<td class="text-right font-mono" data-col="param-${k}">${v}</td>`;
                }).join("");

                return `
                  <tr data-params="${b.Parameters || ""}" data-key="${b.CompositeKey}"${isBest ? ' class="best-row"' : ""}>
                    <td class="font-mono">${benchName}</td>
                    ${extraParamCells}
                    <td class="text-right font-mono"${hm("MeanNs")}>${formatNs(b.MeanNs)}</td>
                    <td class="text-right font-mono"${hm("OpsPerSec")}>${formatOps(b.OpsPerSec)}</td>
                    <td class="text-right font-mono"${hm("ErrorNs")}>${formatNs(b.ErrorNs)}</td>
                    <td class="text-right font-mono"${hm("StdDevNs")}>${formatNs(b.StdDevNs)}</td>
                    <td class="text-right font-mono"${hm("P95Ns")}>${formatNs(b.P95Ns)}</td>
                    <td class="text-right font-mono" data-col="rank">${rankBadge(b.Rank)}</td>
                    ${!allZeroAlloc ? `<td class="text-right font-mono"${hm("AllocatedBytes")}>${allocBadge(b.AllocatedBytes)}</td>` : ""}
                    ${!allZeroGen0 ? `<td class="text-right font-mono">${formatGen(b.Gen0)}</td>` : ""}
                    ${!allZeroGen1 ? `<td class="text-right font-mono">${formatGen(b.Gen1)}</td>` : ""}
                    ${!allZeroGen2 ? `<td class="text-right font-mono">${formatGen(b.Gen2)}</td>` : ""}
                    ${deltaCell}
                  </tr>
                `;
              }).join("");

              const deltaHeader = isComparison ? `<th class="text-right" data-sort="DeltaPercent">Delta <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : "";

              // Parameter filters grouped by method

              const CHART_CAP = 20;
              const TABLE_CAP = 15;
              const chartCapped = classBenchmarks.length > CHART_CAP;
              const tableCapped = classBenchmarks.length > TABLE_CAP;

              const sectionHtml = `
                <div class="class-section mb-8" id="${sectionId}">
                  <div class="flex items-center gap-3 mb-3 cursor-pointer select-none" onclick="toggleSection('${sectionId}')">
                    <i data-lucide="chevron-down" class="w-4 h-4 text-base-content/40 transition-transform" id="toggle-${sectionId}"></i>
                    <h2 class="text-lg font-semibold">${shortName}</h2>
                    <span class="text-base-content/40 text-xs">${classBenchmarks.length} benchmarks</span>
                  </div>
                  <div id="body-${sectionId}">
                    <div class="grid grid-cols-3 gap-3 mb-3">
                      <div class="bg-base-200 border border-base-300 rounded-lg p-4">
                        <div class="text-xs text-base-content/50 flex items-center gap-1 font-medium"><i data-lucide="zap" class="w-3.5 h-3.5"></i> Fastest</div>
                        <div class="text-xl font-bold text-success mt-1">${formatNs(bestMeanNs)}</div>
                        ${statCardDetails(fastestBench)}
                      </div>
                      <div class="bg-base-200 border border-base-300 rounded-lg p-4">
                        <div class="text-xs text-base-content/50 flex items-center gap-1 font-medium"><i data-lucide="snail" class="w-3.5 h-3.5"></i> Slowest</div>
                        <div class="text-xl font-bold text-warning mt-1">${formatNs(worstMeanNs)}</div>
                        ${statCardDetails(slowestBench)}
                      </div>
                      <div class="bg-base-200 border border-base-300 rounded-lg p-4">
                        <div class="text-xs text-base-content/50 flex items-center gap-1 font-medium"><i data-lucide="memory-stick" class="w-3.5 h-3.5"></i> Peak Alloc</div>
                        <div class="text-xl font-bold ${heaviestBench.AllocatedBytes > 0 ? 'text-error' : 'text-base-content/30'} mt-1">${formatBytes(heaviestBench.AllocatedBytes)}</div>
                        ${heaviestBench.AllocatedBytes > 0 ? statCardDetails(heaviestBench) : '<div class="text-xs text-base-content/30 mt-3">No allocations detected</div>'}
                      </div>
                    </div>
                    <div class="flex items-center gap-2 mb-2" id="groupby-${sectionId}"></div>
                    <div class="rounded-lg border border-base-300 overflow-x-auto mb-3" id="tablewrap-${sectionId}">
                      <table class="table table-sm table-zebra col-hover" id="datatable-${sectionId}">
                        <colgroup>
                          <col />${extraParamKeys.map(() => "<col />").join("")}<col /><col /><col /><col /><col /><col />
                          ${!allZeroAlloc ? "<col />" : ""}
                          ${!allZeroGen0 ? "<col />" : ""}
                          ${!allZeroGen1 ? "<col />" : ""}
                          ${!allZeroGen2 ? "<col />" : ""}
                          ${isComparison ? "<col />" : ""}
                        </colgroup>
                        <thead>
                          <tr>
                            <th data-sort="Method"><i data-lucide="flask-conical" class="col-icon"></i> Benchmark <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            ${extraParamKeys.map(k => `<th class="text-right" data-sort="Param_${k}"><i data-lucide="settings-2" class="col-icon"></i> ${k} <i data-lucide="arrow-up-down" class="sort-icon"></i></th>`).join("")}
                            <th class="text-right" data-sort="MeanNs"><i data-lucide="bar-chart-2" class="col-icon"></i> Mean <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="OpsPerSec"><i data-lucide="zap" class="col-icon"></i> Ops/sec <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="ErrorNs"><i data-lucide="shield-alert" class="col-icon"></i> Error <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="StdDevNs"><i data-lucide="activity" class="col-icon"></i> StdDev <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="P95Ns"><i data-lucide="percent" class="col-icon"></i> P95 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="Rank"><i data-lucide="trophy" class="col-icon"></i> Rank <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            ${!allZeroAlloc ? `<th class="text-right" data-sort="AllocatedBytes"><i data-lucide="memory-stick" class="col-icon"></i> Allocated <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen0 ? `<th class="text-right" data-sort="Gen0"><i data-lucide="recycle" class="col-icon"></i> Gen0 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen1 ? `<th class="text-right" data-sort="Gen1"><i data-lucide="recycle" class="col-icon"></i> Gen1 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen2 ? `<th class="text-right" data-sort="Gen2"><i data-lucide="recycle" class="col-icon"></i> Gen2 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${deltaHeader}
                          </tr>
                        </thead>
                        <tbody id="table-${sectionId}">${tableRows}</tbody>
                      </table>
                      ${tableCapped ? `
                      <div class="flex justify-center py-2 border-t border-base-300" id="table-cap-${sectionId}">
                        <button class="btn btn-xs btn-ghost gap-1" data-table-expand="${sectionId}">
                          <span>Showing ${TABLE_CAP} of ${classBenchmarks.length}</span>
                          <i data-lucide="chevron-down" class="w-3.5 h-3.5"></i>
                        </button>
                      </div>` : ""}
                    </div>
                    <div class="hidden flex flex-col gap-2 mb-3" id="grouped-${sectionId}"></div>
                    <details class="collapse collapse-arrow bg-base-200 border border-base-300 rounded-lg mb-3" id="chartdetails-${sectionId}">
                      <summary class="collapse-title text-sm font-semibold min-h-0 py-2.5 px-4 flex items-center gap-2">
                        <i data-lucide="bar-chart-horizontal" class="w-4 h-4"></i> Chart
                      </summary>
                      <div class="collapse-content p-0!">
                        <div class="p-4">
                          <div class="flex items-center gap-2 mb-3">
                            <div class="flex flex-wrap gap-1.5" data-section="${sectionId}">
                              ${metrics.filter(m => classBenchmarks.some(b => (b[m.key] ?? 0) !== 0)).map((m, i) => `<button class="btn btn-xs ${i === 0 ? 'btn-primary' : 'btn-ghost'}" data-metric="${m.key}" data-section="${sectionId}">${m.label}</button>`).join("")}
                            </div>
                            <button class="btn btn-xs btn-ghost gap-1" data-log-toggle="${sectionId}" title="Toggle log scale">
                              <i data-lucide="scaling" class="w-3.5 h-3.5"></i>
                              <span class="text-xs font-mono" data-log-label="${sectionId}">linear</span>
                            </button>
                            ${chartCapped ? `<button class="btn btn-xs btn-ghost gap-1" data-chart-expand="${sectionId}" title="Show all benchmarks in chart">
                              <i data-lucide="unfold-horizontal" class="w-3.5 h-3.5"></i>
                              <span class="text-xs">Show all</span>
                            </button>` : ""}
                          </div>
                          <div id="chartwrap-${sectionId}" style="height: 260px; position: relative; overflow-y: auto;">
                            <div id="chartinner-${sectionId}" style="height: 100%; position: relative;">
                              <canvas id="chart-${sectionId}"></canvas>
                            </div>
                          </div>
                          ${chartCapped ? `<div class="text-xs text-base-content/40 mt-1 text-center" id="chartcap-label-${sectionId}">Showing top ${CHART_CAP} of ${classBenchmarks.length}</div>` : ""}
                        </div>
                      </div>
                    </details>
                  </div>
                </div>
              `;

              classSections.insertAdjacentHTML("beforeend", sectionHtml);

              // Apply initial table row cap
              if (tableCapped) {
                const tbody = document.getElementById("table-" + sectionId);
                const rows = tbody.querySelectorAll("tr[data-key]");
                rows.forEach((r, i) => { if (i >= TABLE_CAP) r.classList.add("hidden"); });
              }
            });

            // ── Table row cap expand/collapse ─────────────────────────────────

            const tableExpandState = {};

            document.querySelectorAll("[data-table-expand]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.tableExpand;
                const expanded = tableExpandState[sectionId] = !tableExpandState[sectionId];
                const tbody = document.getElementById("table-" + sectionId);
                const rows = tbody.querySelectorAll("tr[data-key]");
                const TABLE_CAP = 15;

                rows.forEach((r, i) => {
                  if (i >= TABLE_CAP) r.classList.toggle("hidden", !expanded);
                });

                const total = rows.length;
                const label = btn.querySelector("span");
                const icon = btn.querySelector("[data-lucide]");
                if (expanded) {
                  label.textContent = "Collapse";
                  if (icon) icon.setAttribute("data-lucide", "chevron-up");
                } else {
                  label.textContent = `Showing ${TABLE_CAP} of ${total}`;
                  if (icon) icon.setAttribute("data-lucide", "chevron-down");
                }
                lucide.createIcons();
              });
            });

            // ── Group by controls ──────────────────────────────────────────────

            function parseParams(paramStr) {
              if (!paramStr) return {};
              const obj = {};
              paramStr.split("&").forEach(kv => {
                const [k, ...rest] = kv.split("=");
                if (k) obj[k.trim()] = rest.join("=").trim();
              });
              return obj;
            }

            function getParamKeys(sectionBenchmarks) {
              const keys = new Set();
              sectionBenchmarks.forEach(b => {
                Object.keys(parseParams(b.Parameters)).forEach(k => keys.add(k));
              });
              return [...keys];
            }

            // Build group-by dropdowns for each section
            classes.forEach(cls => {
              const sectionId = "section-" + cls;
              const sectionBenchmarks = benchmarks.filter(b => b.Type === cls);
              const paramKeys = getParamKeys(sectionBenchmarks);
              const container = document.getElementById("groupby-" + sectionId);
              if (!container || paramKeys.length === 0) return;

              // Add "Method" as a grouping option alongside param keys
              const options = ["none", "Method", ...paramKeys];

              container.innerHTML = `
                <i data-lucide="layers" class="w-3.5 h-3.5 text-base-content/40"></i>
                <span class="text-xs text-base-content/50">Group by</span>
                <select class="select select-xs select-bordered font-mono" data-groupby="${sectionId}">
                  ${options.map(o => `<option value="${o}">${o}</option>`).join("")}
                </select>
              `;
            });

            function applyGroupBy(sectionId, groupKey) {
              const tableWrap = document.getElementById("tablewrap-" + sectionId);
              const groupedWrap = document.getElementById("grouped-" + sectionId);
              if (!tableWrap || !groupedWrap) return;

              const table = document.getElementById("datatable-" + sectionId);
              const cls = sectionId.replace("section-", "");
              const sectionBenchmarks = benchmarks.filter(b => b.Type === cls);

              if (groupKey === "none") {
                tableWrap.classList.remove("hidden");
                groupedWrap.classList.add("hidden");
                groupedWrap.innerHTML = "";
                // Reset chart to show all section benchmarks
                const metricKey = chartMetricState[sectionId] || "MeanNs";
                buildChart(sectionId, metricKey);
                return;
              }

              // Group benchmarks by key
              const groups = new Map();
              sectionBenchmarks.forEach(b => {
                let val;
                if (groupKey === "Method") val = b.Method;
                else val = parseParams(b.Parameters)[groupKey] || "\u2014";
                if (!groups.has(val)) groups.set(val, []);
                groups.get(val).push(b);
              });

              // Clone thead and colgroup from the original table
              const theadHtml = table.querySelector("thead").outerHTML;
              const colgroupHtml = table.querySelector("colgroup").outerHTML;

              // Build a map of CompositeKey -> row outerHTML from original table
              // Clone and strip "hidden" class (added by table row cap) so grouped rows are visible
              const rowMap = new Map();
              table.querySelectorAll("tbody tr[data-key]").forEach(r => {
                const clone = r.cloneNode(true);
                clone.classList.remove("hidden");
                rowMap.set(r.dataset.key, clone.outerHTML);
              });

              // Build collapsible group cards with top 3 + worst 1 preview
              const GROUP_PREVIEW = 3;
              let html = "";
              groups.forEach((groupBenchmarks, groupVal) => {
                // Sort by MeanNs ascending (fastest first)
                const sorted = [...groupBenchmarks].sort((a, b) => a.MeanNs - b.MeanNs);
                const needsCap = sorted.length > GROUP_PREVIEW + 1;
                let previewKeys, hiddenKeys;
                if (needsCap) {
                  const top = sorted.slice(0, GROUP_PREVIEW);
                  const worst = sorted[sorted.length - 1];
                  const topSet = new Set(top.map(b => b.CompositeKey));
                  if (!topSet.has(worst.CompositeKey)) {
                    previewKeys = [...top.map(b => b.CompositeKey), worst.CompositeKey];
                  } else {
                    previewKeys = top.map(b => b.CompositeKey);
                  }
                  const previewSet = new Set(previewKeys);
                  hiddenKeys = sorted.filter(b => !previewSet.has(b.CompositeKey)).map(b => b.CompositeKey);
                } else {
                  previewKeys = sorted.map(b => b.CompositeKey);
                  hiddenKeys = [];
                }

                const groupRankIcons = { 0: "🥇", 1: "🥈", 2: "🥉" };
                const worstKey = needsCap ? sorted[sorted.length - 1].CompositeKey : null;
                const previewRowsHtml = previewKeys.map((k, i) => {
                  let row = rowMap.get(k) || "";
                  let icon = groupRankIcons[i] || "";
                  if (k === worstKey && !groupRankIcons[i]) icon = "🐢";
                  if (icon) {
                    const title = icon === "🐢" ? "Slowest in group" : `#${i+1} fastest in group`;
                    const badge = ` <span title="${title}">${icon}</span>`;
                    row = row.replace(/(<td[^>]*data-col="rank"[^>]*>)(.*?)(<\/td>)/, `$1$2${badge}$3`);
                  }
                  return row;
                }).join("");
                const hiddenRowsHtml = hiddenKeys.map(k => {
                  const r = rowMap.get(k) || "";
                  return r.replace("<tr ", '<tr class="group-hidden-row hidden" ');
                }).join("");
                const expandBtn = needsCap
                  ? `<button class="btn btn-xs btn-ghost gap-1 group-expand-btn ml-auto" data-group-total="${sorted.length}">
                       <i data-lucide="chevrons-down" class="w-3 h-3"></i>
                       Show all ${sorted.length}
                     </button>`
                  : "";

                html += `
                  <details class="collapse collapse-arrow bg-base-200 border border-base-300 rounded-lg" data-group-val="${groupVal.replace(/"/g, '&quot;')}">
                    <summary class="collapse-title flex items-center gap-2 text-sm font-semibold min-h-0 py-2.5 px-4">
                      ${groupVal}
                      <span class="badge badge-xs badge-primary">${groupBenchmarks.length}</span>
                      ${expandBtn}
                    </summary>
                    <div class="collapse-content p-0!">
                      <div class="overflow-x-auto">
                        <table class="table table-sm table-zebra">
                          ${colgroupHtml}
                          ${theadHtml}
                          <tbody>${previewRowsHtml}${hiddenRowsHtml}</tbody>
                        </table>
                      </div>
                    </div>
                  </details>`;
              });

              tableWrap.classList.add("hidden");
              groupedWrap.classList.remove("hidden");
              groupedWrap.innerHTML = html;

              applyHeatmap();
              lucide.createIcons();

              // Rebuild chart when a group is opened/closed
              groupedWrap.querySelectorAll("details[data-group-val] > summary").forEach(summary => {
                summary.addEventListener("click", () => {
                  const d = summary.closest("details");
                  const metricKey = chartMetricState[sectionId] || "MeanNs";
                  // After click, open state will toggle — use setTimeout to read the new state
                  setTimeout(() => {
                    if (d.open) {
                      const groupBenchmarks = groups.get(d.dataset.groupVal) || [];
                      buildChart(sectionId, metricKey, groupBenchmarks);
                    } else {
                      buildChart(sectionId, metricKey, sectionBenchmarks);
                    }
                  }, 0);
                });
              });
            }

            document.querySelectorAll("[data-groupby]").forEach(sel => {
              sel.addEventListener("change", () => {
                applyGroupBy(sel.dataset.groupby, sel.value);
                lucide.createIcons();
              });
            });

            // ── Chart.js instances ────────────────────────────────────────────

            const chartInstances = {};
            const chartLogState = {};
            const chartMetricState = {};

            function getChartTextColor() {
              return getComputedStyle(document.body).getPropertyValue("color") || "#e0e0e0";
            }

            function getChartGridColor() {
              return "rgba(128, 128, 128, 0.15)";
            }

            const CHART_CAP = 20;
            const chartExpandState = {};

            function buildChart(sectionId, metricKey, filterBenchmarks) {
              const cls = sectionId.replace("section-", "");
              const allSectionBenchmarks = filterBenchmarks || benchmarks.filter(b => b.Type === cls);
              const metric = metrics.find(m => m.key === metricKey);
              if (!metric) return;

              const isLog = chartLogState[sectionId] || false;
              chartMetricState[sectionId] = metricKey;

              const expanded = chartExpandState[sectionId] || false;
              const needsCap = allSectionBenchmarks.length > CHART_CAP;

              // Sort by metric to show top N, use ascending for time metrics, descending for ops
              let sorted = [...allSectionBenchmarks];
              if (metricKey === "OpsPerSec") {
                sorted.sort((a, b) => (b[metricKey] ?? 0) - (a[metricKey] ?? 0));
              } else {
                sorted.sort((a, b) => (a[metricKey] ?? 0) - (b[metricKey] ?? 0));
              }
              const sectionBenchmarks = (needsCap && !expanded) ? sorted.slice(0, CHART_CAP) : sorted;

              // Resize chart container height based on bar count
              const wrapEl = document.getElementById("chartwrap-" + sectionId);
              const innerEl = document.getElementById("chartinner-" + sectionId);
              const barHeight = 28;
              const chartH = Math.max(200, sectionBenchmarks.length * barHeight);
              if (wrapEl) wrapEl.style.height = chartH + "px";
              if (innerEl) {
                innerEl.style.minWidth = "";
                innerEl.style.height = "100%";
              }

              // Update cap label
              const capLabel = document.getElementById("chartcap-label-" + sectionId);
              if (capLabel) {
                if (!needsCap) {
                  capLabel.classList.add("hidden");
                } else if (expanded) {
                  capLabel.textContent = `Showing all ${allSectionBenchmarks.length} (sorted by ${metric.label})`;
                } else {
                  capLabel.textContent = `Showing top ${CHART_CAP} of ${allSectionBenchmarks.length} by ${metric.label}`;
                }
              }

              const labels = sectionBenchmarks.map(b => shortLabel(b));
              const values = sectionBenchmarks.map(b => b[metricKey] ?? 0);
              const n = sectionBenchmarks.length;
              const bgColors = sectionBenchmarks.map((_, i) => barColorAlpha(i, n, 0.8));
              const borderColors = sectionBenchmarks.map((_, i) => barColor(i, n));
              const textColor = getChartTextColor();

              if (chartInstances[sectionId]) {
                chartInstances[sectionId].destroy();
              }

              const canvas = document.getElementById("chart-" + sectionId);
              if (!canvas) return;

              chartInstances[sectionId] = new Chart(canvas, {
                type: "bar",
                data: {
                  labels,
                  datasets: [{
                    data: values,
                    backgroundColor: bgColors,
                    borderColor: borderColors,
                    borderWidth: 1,
                    borderRadius: 3,
                  }]
                },
                options: {
                  indexAxis: "y",
                  responsive: true,
                  maintainAspectRatio: false,
                  animation: { duration: 300 },
                  plugins: {
                    legend: { display: false },
                    tooltip: {
                      callbacks: {
                        title: (ctx) => benchLabel(sectionBenchmarks[ctx[0].dataIndex]),
                        label: (ctx) => metric.format(ctx.raw),
                      },
                      titleFont: { family: "monospace", size: 11 },
                      bodyFont: { family: "monospace", size: 13, weight: "bold" },
                    }
                  },
                  scales: {
                    x: {
                      type: isLog ? "logarithmic" : "linear",
                      ticks: {
                        color: textColor,
                        font: { family: "monospace", size: 10 },
                        callback: (v) => metric.format(v),
                      },
                      grid: { color: getChartGridColor() },
                    },
                    y: {
                      ticks: {
                        color: textColor,
                        font: { family: "monospace", size: 10 },
                      },
                      grid: { display: false },
                    }
                  }
                }
              });
            }

            // Initialize all charts
            classes.forEach(cls => {
              const sectionId = "section-" + cls;
              buildChart(sectionId, "MeanNs");
            });

            // Metric pill switching
            document.querySelectorAll("[data-metric]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.section;
                const wrapper = btn.parentElement;
                wrapper.querySelectorAll("[data-metric]").forEach(b => {
                  b.classList.remove("btn-primary");
                  b.classList.add("btn-ghost");
                });
                btn.classList.remove("btn-ghost");
                btn.classList.add("btn-primary");
                buildChart(sectionId, btn.dataset.metric);
              });
            });

            // Log scale toggle
            document.querySelectorAll("[data-log-toggle]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.logToggle;
                chartLogState[sectionId] = !chartLogState[sectionId];
                const label = btn.querySelector("[data-log-label]");
                if (label) label.textContent = chartLogState[sectionId] ? "log" : "linear";
                btn.classList.toggle("btn-primary", chartLogState[sectionId]);
                btn.classList.toggle("btn-ghost", !chartLogState[sectionId]);
                buildChart(sectionId, chartMetricState[sectionId] || "MeanNs");
              });
            });

            // Chart expand toggle
            document.querySelectorAll("[data-chart-expand]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.chartExpand;
                chartExpandState[sectionId] = !chartExpandState[sectionId];
                const expanded = chartExpandState[sectionId];
                const label = btn.querySelector("span");
                if (label) label.textContent = expanded ? "Top 20" : "Show all";
                btn.classList.toggle("btn-primary", expanded);
                btn.classList.toggle("btn-ghost", !expanded);
                buildChart(sectionId, chartMetricState[sectionId] || "MeanNs");
              });
            });

            // ── Collapsible sections ────────────────────────────────────────────

            function toggleSection(sectionId) {
              const body = document.getElementById("body-" + sectionId);
              const toggle = document.getElementById("toggle-" + sectionId);
              body.classList.toggle("collapsed-body");
              toggle.classList.toggle("collapsed-toggle");
            }

            // ── Expand/Collapse all ───────────────────────────────────────────

            let allExpanded = true;
            document.getElementById("toggle-all").addEventListener("click", () => {
              allExpanded = !allExpanded;
              document.querySelectorAll(".class-section").forEach(section => {
                const id = section.id;
                const body = document.getElementById("body-" + id);
                const toggle = document.getElementById("toggle-" + id);
                if (allExpanded) {
                  body.classList.remove("collapsed-body");
                  toggle.classList.remove("collapsed-toggle");
                } else {
                  body.classList.add("collapsed-body");
                  toggle.classList.add("collapsed-toggle");
                }
              });
              const icon = document.getElementById("toggle-all-icon");
              icon.setAttribute("data-lucide", allExpanded ? "unfold-vertical" : "fold-vertical");
              lucide.createIcons();
            });

            // ── Sortable tables (delegated for dynamic sub-tables) ──────────────

            document.addEventListener("click", (e) => {
              const th = e.target.closest("th[data-sort]");
              if (!th) return;

              const table = th.closest("table");
              const tbody = table.querySelector("tbody");
              const key   = th.dataset.sort;
              const isAsc = th.classList.contains("sort-asc");

              // Reset all headers in this table
              table.querySelectorAll("thead th[data-sort]").forEach(h => {
                h.classList.remove("sort-asc", "sort-desc");
                const icon = h.querySelector(".sort-icon");
                if (icon) { icon.setAttribute("data-lucide", "arrow-up-down"); }
              });

              const dir = isAsc ? -1 : 1;
              const newDir = isAsc ? "sort-desc" : "sort-asc";
              th.classList.add(newDir);
              const activeIcon = th.querySelector(".sort-icon");
              if (activeIcon) {
                activeIcon.setAttribute("data-lucide", newDir === "sort-asc" ? "arrow-up" : "arrow-down");
              }
              lucide.createIcons();

              // Build a benchmark lookup by CompositeKey
              const benchByKey = new Map();
              benchmarks.forEach(b => {
                if (!benchByKey.has(b.CompositeKey)) benchByKey.set(b.CompositeKey, b);
              });

              // Pair every DOM row with its sort value (handles duplicates)
              const rows = [...tbody.querySelectorAll("tr[data-key]")];
              const sortable = rows.map(row => {
                const b = benchByKey.get(row.dataset.key);
                let value = 0;
                if (b) {
                  if (key.startsWith("Param_")) {
                    const pKey = key.slice(6);
                    const pVal = parseParams(b.Parameters)[pKey] || "";
                    value = isNaN(Number(pVal)) ? pVal : Number(pVal);
                  } else {
                    value = b[key] ?? 0;
                  }
                }
                return { row, value };
              });

              sortable.sort((a, b) => {
                return typeof a.value === "string"
                  ? dir * a.value.localeCompare(b.value)
                  : dir * (a.value - b.value);
              });

              // Reorder all DOM rows
              sortable.forEach(({ row }) => tbody.appendChild(row));
            });

            // ── Group expand/collapse (delegated) ────────────────────────────
            document.addEventListener("click", (e) => {
              const btn = e.target.closest(".group-expand-btn");
              if (!btn) return;
              e.preventDefault();
              e.stopPropagation();
              const details = btn.closest("details");
              const tbody = details?.querySelector("tbody");
              if (!tbody) return;
              const hidden = tbody.querySelectorAll(".group-hidden-row.hidden");
              const total = btn.dataset.groupTotal;
              if (hidden.length > 0) {
                hidden.forEach(r => r.classList.remove("hidden"));
                btn.innerHTML = '<i data-lucide="chevrons-up" class="w-3 h-3"></i> Top only';
                lucide.createIcons();
                applyHeatmap();
              } else {
                tbody.querySelectorAll(".group-hidden-row").forEach(r => r.classList.add("hidden"));
                btn.innerHTML = `<i data-lucide="chevrons-down" class="w-3 h-3"></i> Show all ${total}`;
                lucide.createIcons();
              }
            });

            // ── Column hover highlight ─────────────────────────────────────────

            document.querySelectorAll("table.col-hover").forEach(table => {
              const cols = table.querySelectorAll("colgroup col");
              table.addEventListener("mouseover", e => {
                const td = e.target.closest("td, th");
                if (!td) return;
                const idx = td.cellIndex;
                cols.forEach(c => c.classList.remove("hover"));
                if (cols[idx]) cols[idx].classList.add("hover");
              });
              table.addEventListener("mouseleave", () => {
                cols.forEach(c => c.classList.remove("hover"));
              });
            });

            // ── URL hash navigation ─────────────────────────────────────────────

            if (location.hash) {
              const target = document.querySelector(location.hash);
              if (target) {
                setTimeout(() => target.scrollIntoView({ behavior: "smooth" }), 100);
              }
            }

            // ── Theme + heatmap + zoom (unified) ──────────────────────────────

            (function() {
              const root   = document.documentElement;
              const select = document.getElementById("theme-select");
              const hToggle = document.getElementById("heatmap-toggle");
              const hLabel  = document.getElementById("heatmap-label");
              const zoomIn  = document.getElementById("zoom-in");
              const zoomOut = document.getElementById("zoom-out");
              const zoomLabel = document.getElementById("zoom-label");

              const darkThemes = new Set(["dark","night","abyss","dim","sunset","dracula","business","synthwave","cyberpunk","halloween","forest","aqua","black","luxury","coffee","nord"]);

              // Theme
              const storedTheme = localStorage.getItem("bdr-theme") || "dark";
              const resolved = storedTheme === "light" ? "corporate" : storedTheme;
              root.setAttribute("data-theme", resolved);
              select.value = resolved;

              select.addEventListener("change", () => {
                root.setAttribute("data-theme", select.value);
                localStorage.setItem("bdr-theme", select.value);
                applyHeatmap();
                // Rebuild all charts so text/grid colors match new theme
                Object.keys(chartInstances).forEach(sid => {
                  buildChart(sid, chartMetricState[sid] || "MeanNs");
                });
              });

              // Heatmap
              const storedHeat = localStorage.getItem("bdr-heatmap") || "cells";
              hToggle.checked = storedHeat === "text";
              hLabel.textContent = storedHeat === "text" ? "text" : "cells";

              hToggle.addEventListener("change", () => {
                const mode = hToggle.checked ? "text" : "cells";
                hLabel.textContent = mode;
                localStorage.setItem("bdr-heatmap", mode);
                applyHeatmap();
              });

              function applyHeatmap() {
                const isDark = darkThemes.has(root.getAttribute("data-theme"));
                const lighten = isDark ? 0.65 : 0.4;
                const textMode = hToggle.checked;
                document.body.classList.toggle("heatmap-text", textMode);

                document.querySelectorAll("td[data-heatmap]").forEach(td => {
                  const ratio = parseFloat(td.dataset.heatRatio);
                  if (isNaN(ratio)) return;
                  const c = heatmapColorFromRatio(ratio, lighten);
                  if (textMode) {
                    td.style.backgroundColor = "";
                    td.style.color = c;
                  } else {
                    td.style.backgroundColor = c;
                    td.style.color = isDark ? "#1a1a2e" : "#1a1a2e";
                  }
                });
              }

              applyHeatmap();

              // Zoom
              const storedZoom = parseInt(localStorage.getItem("bdr-zoom") || "16", 10);
              let fontSize = storedZoom;
              applyZoom();

              zoomIn.addEventListener("click", () => { fontSize = Math.min(fontSize + 1, 24); applyZoom(); });
              zoomOut.addEventListener("click", () => { fontSize = Math.max(fontSize - 1, 10); applyZoom(); });

              function applyZoom() {
                document.documentElement.style.fontSize = fontSize + "px";
                zoomLabel.textContent = fontSize;
                localStorage.setItem("bdr-zoom", fontSize);
              }
            })();

            // ── Back-to-top FAB ───────────────────────────────────────────────

            const fab = document.getElementById("fab-top");
            window.addEventListener("scroll", () => {
              fab.classList.toggle("visible", window.scrollY > 300);
            }, { passive: true });
            fab.addEventListener("click", () => {
              window.scrollTo({ top: 0, behavior: "smooth" });
            });

            lucide.createIcons();
            </script>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
