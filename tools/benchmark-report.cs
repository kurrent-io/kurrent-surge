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
            <style>
              html { scroll-behavior: smooth; }
              .bar-fill { transition: width 0.3s ease; }
              .bar-fill-baseline {
                position: absolute;
                top: 0;
                height: 100%;
                border-radius: 0.25rem;
                opacity: 0.3;
                border-right: 2px dashed #9ca3af;
              }
              .collapsed-body { display: none; }
              .collapsed-toggle { transform: rotate(-90deg); }
              th[data-sort] { cursor: pointer; user-select: none; white-space: nowrap; }
              th[data-sort] .sort-icon { display: inline-block; width: 14px; height: 14px; vertical-align: middle; margin-left: 2px; opacity: 0.3; }
              th[data-sort].sort-asc .sort-icon, th[data-sort].sort-desc .sort-icon { opacity: 1; }
              td[data-heatmap] { transition: background-color 0.15s ease, color 0.15s ease; }
              .heatmap-text td[data-heatmap] { background-color: transparent !important; }
              tr.best-row td { font-weight: 700; }
              table.col-hover col.hover { background: oklch(0.5 0 0 / 0.04); }
            </style>
            </head>
            <body class="bg-base-100 text-base-content leading-relaxed p-8 max-w-[1400px] mx-auto">

            <header class="flex justify-between items-start mb-6 pb-5 border-b border-base-300">
              <div>
                <h1 class="text-xl font-semibold">Kurrent.Client Benchmark Report
                  <span class="badge badge-soft {{(data.IsComparison ? "badge-warning" : "badge-info")}} ml-2">
                    {{(data.IsComparison ? "comparison" : "single-run")}}
                  </span>
                </h1>
                <p class="text-base-content/60 text-sm">Generated {{timestamp}}</p>
              </div>
              <div class="flex items-center gap-4">
              <div class="flex items-center gap-1" title="Text size">
                <button class="btn btn-xs btn-ghost btn-square" id="zoom-out"><i data-lucide="minus" class="w-3 h-3"></i></button>
                <span class="text-xs font-mono text-base-content/50 w-8 text-center" id="zoom-label">16</span>
                <button class="btn btn-xs btn-ghost btn-square" id="zoom-in"><i data-lucide="plus" class="w-3 h-3"></i></button>
              </div>
              <label class="flex items-center gap-1.5 cursor-pointer text-xs" title="Heatmap mode: color cells or text">
                <i data-lucide="thermometer" class="w-3.5 h-3.5 text-base-content/40"></i>
                <span class="text-base-content/50 font-mono" id="heatmap-label">cells</span>
                <input type="checkbox" class="toggle toggle-xs" id="heatmap-toggle" />
              </label>
              <label class="flex items-center gap-1.5 cursor-pointer">
                <i data-lucide="palette" class="w-4 h-4 text-base-content/40"></i>
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
              </label>
              </div>
            </header>

            <div id="env-section"></div>
            <div id="stats-row"></div>
            <div id="highlights-section"></div>
            <div id="search-row"></div>
            <div class="flex flex-wrap gap-2 mb-6" id="section-nav"></div>
            <div id="regression-section"></div>
            <div id="class-sections"></div>

            <footer class="text-center text-base-content/40 text-xs pt-4 border-t border-base-300">
              <code class="font-mono">Kurrent.Client</code> &middot; Generated by benchmark-report
            </footer>

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
              const medals = { 1: ["🥇", "badge-success"], 2: ["🥈", "badge-info"], 3: ["🥉", "badge-warning"] };
              if (medals[rank]) {
                const [medal, cls] = medals[rank];
                return `<span class="badge badge-xs badge-soft ${cls}">${medal} ${rank}</span>`;
              }
              return `<span class="badge badge-xs">${rank}</span>`;
            }

            function heatmapRatio(value, min, max) {
              if (min === max) return -1;
              return (max - value) / (max - min);  // 1.0 = best (lowest), 0.0 = worst (highest)
            }

            function heatmapColorFromRatio(ratio, lighten) {
              if (ratio < 0) return "";
              let r, g;
              if (ratio <= 0.5) {
                const t = ratio * 2;
                r = 255;
                g = Math.round(255 * Math.sqrt(t));
              } else {
                const t = (ratio - 0.5) * 2;
                r = Math.round(255 * (1 - t * t));
                g = 255;
              }
              r = Math.round(r + (255 - r) * lighten);
              g = Math.round(g + (255 - g) * lighten);
              const b = Math.round(0 + 255 * lighten);
              return `rgb(${r}, ${g}, ${b})`;
            }

            function allocBadge(bytes) {
              const cls = bytes === 0 ? "badge-ghost" : "badge-warning";
              return `<span class="badge badge-xs badge-soft ${cls}">${formatBytes(bytes)}</span>`;
            }

            function benchLabel(b) {
              return b.Parameters ? `${b.Method}(${b.Parameters})` : b.Method;
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
              if (total <= 1) return "hsl(142, 65%, 50%)";
              const hue = 142 - (index / (total - 1)) * 142;
              return `hsl(${hue}, 65%, 50%)`;
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

            // ── Stats row ───────────────────────────────────────────────────────

            {
              const means = benchmarks.map(b => b.MeanNs);
              const fastest = Math.min(...means);
              const slowest = Math.max(...means);
              const totalAlloc = benchmarks.reduce((s, b) => s + b.AllocatedBytes, 0);

              let statsHtml = `
                <div class="stats stats-horizontal bg-base-200 border border-base-300 w-full mb-6 overflow-x-auto">
                  <div class="stat">
                    <div class="stat-title">Benchmarks</div>
                    <div class="stat-value text-lg">${benchmarks.length}</div>
                  </div>
                  <div class="stat">
                    <div class="stat-title">Fastest</div>
                    <div class="stat-value text-lg text-success">${formatNs(fastest)}</div>
                  </div>
                  <div class="stat">
                    <div class="stat-title">Slowest</div>
                    <div class="stat-value text-lg text-warning">${formatNs(slowest)}</div>
                  </div>
                  <div class="stat">
                    <div class="stat-title">Total Allocations</div>
                    <div class="stat-value text-lg">${formatBytes(totalAlloc)}</div>
                  </div>
              `;

              if (isComparison && comparisons) {
                const regressions = comparisons.filter(c => c.Classification === "Regressed").length;
                const improvements = comparisons.filter(c => c.Classification === "Improved").length;
                statsHtml += `
                  <div class="stat">
                    <div class="stat-title">Regressions</div>
                    <div class="stat-value text-lg ${regressions > 0 ? 'text-error' : 'text-success'}">${regressions}</div>
                  </div>
                  <div class="stat">
                    <div class="stat-title">Improvements</div>
                    <div class="stat-value text-lg ${improvements > 0 ? 'text-success' : 'text-base-content/60'}">${improvements}</div>
                  </div>
                `;
              }

              statsHtml += `</div>`;

              document.getElementById("stats-row").innerHTML = statsHtml;
            }

            // ── Highlights section ──────────────────────────────────────────────

            {
              function top5(arr) { return arr.slice(0, 5); }

              const bySpeed    = [...benchmarks].sort((a, b) => a.MeanNs - b.MeanNs);
              const fastest5   = top5(bySpeed);
              const slowest5   = top5([...benchmarks].sort((a, b) => b.MeanNs - a.MeanNs));
              const memory5    = top5([...benchmarks].filter(b => b.AllocatedBytes > 0)
                                       .sort((a, b) => b.AllocatedBytes - a.AllocatedBytes));

              function highlightRows(items, valueGetter, valueColor) {
                return items.map(b => `
                  <div class="flex items-center justify-between px-4 py-2 gap-2">
                    <span class="font-mono text-xs text-base-content/60 truncate min-w-0" title="${benchLabel(b)}">${shortLabel(b)}</span>
                    <span class="font-mono text-sm font-semibold whitespace-nowrap ${valueColor}">${valueGetter(b)}</span>
                  </div>
                `).join("");
              }

              const fastestCard = `
                <div class="card card-xs bg-base-200 border border-base-300">
                  <div class="card-body p-0">
                    <h3 class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-success border-b border-base-300">Top 5 Fastest</h3>
                    <div class="flex flex-col">
                      ${highlightRows(fastest5, b => formatNs(b.MeanNs), "text-success")}
                    </div>
                  </div>
                </div>`;

              const slowestCard = `
                <div class="card card-xs bg-base-200 border border-base-300">
                  <div class="card-body p-0">
                    <h3 class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-error border-b border-base-300">Top 5 Slowest</h3>
                    <div class="flex flex-col">
                      ${highlightRows(slowest5, b => formatNs(b.MeanNs), "text-error")}
                    </div>
                  </div>
                </div>`;

              const memoryRows = memory5.length > 0
                ? highlightRows(memory5, b => formatBytes(b.AllocatedBytes), "text-warning")
                : `<div class="flex items-center px-4 py-2"><span class="font-mono text-xs text-success">All benchmarks allocate zero</span></div>`;

              const memoryCard = `
                <div class="card card-xs bg-base-200 border border-base-300">
                  <div class="card-body p-0">
                    <h3 class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-warning border-b border-base-300">Top 5 Most Memory</h3>
                    <div class="flex flex-col">
                      ${memoryRows}
                    </div>
                  </div>
                </div>`;

              document.getElementById("highlights-section").innerHTML = `
                <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                  ${fastestCard}
                  ${slowestCard}
                  ${memoryCard}
                </div>
              `;
            }

            // ── Section navigation ──────────────────────────────────────────────

            document.getElementById("section-nav").innerHTML = classes.map(c => {
              const shortName = c.split(".").pop();
              return `<a class="badge badge-outline badge-sm hover:badge-primary transition-colors no-underline" href="#section-${c}">${shortName}</a>`;
            }).join("");

            // ── Global search ───────────────────────────────────────────────────

            document.getElementById("search-row").innerHTML = `
              <input class="input input-bordered w-full mb-4" id="global-search" type="search"
                     placeholder="Search benchmarks..." autocomplete="off">
            `;

            document.getElementById("global-search").addEventListener("input", e => {
              const term = e.target.value.toLowerCase().trim();

              document.querySelectorAll(".class-section").forEach(section => {
                let sectionVisible = false;

                section.querySelectorAll("[data-params]").forEach(el => {
                  if (el.tagName === "TABLE" || el.tagName === "THEAD") return;
                  const labelEl = el.querySelector(".bar-label, td:first-child");
                  const text = (labelEl?.textContent ?? "").toLowerCase();
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
                    <table class="table table-xs">
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
              const hasParams = params.length > 1;

              const allZeroAlloc = classBenchmarks.every(b => b.AllocatedBytes === 0);
              const allZeroGen0 = classBenchmarks.every(b => b.Gen0 === 0);
              const allZeroGen1 = classBenchmarks.every(b => b.Gen1 === 0);
              const allZeroGen2 = classBenchmarks.every(b => b.Gen2 === 0);

              // Bar chart
              const barRows = classBenchmarks.map((b, i) => {
                const pct = (b.MeanNs / barMax) * 100;
                const color = barColor(i, classBenchmarks.length);
                let baselineBar = "";

                if (isComparison) {
                  const comp = compMap[b.CompositeKey];
                  if (comp?.Baseline) {
                    const bPct = (comp.Baseline.MeanNs / barMax) * 100;
                    baselineBar = `<div class="bar-fill-baseline" style="width: ${bPct}%; background: ${color};"></div>`;
                  }
                }

                return `
                  <div class="grid items-center gap-4 h-8" style="grid-template-columns: 200px 1fr 100px" data-params="${b.Parameters || ""}" data-key="${b.CompositeKey}">
                    <div class="bar-label font-mono text-xs text-base-content/60 text-right whitespace-nowrap overflow-hidden text-ellipsis" title="${benchLabel(b)}">${shortLabel(b)}</div>
                    <div class="h-6 rounded bg-base-300/50 relative overflow-hidden">
                      ${baselineBar}
                      <div class="bar-fill h-full rounded opacity-80" style="width: ${pct}%; background: ${color};"></div>
                    </div>
                    <div class="bar-value font-mono text-sm text-right whitespace-nowrap text-base-content/70">${formatNs(b.MeanNs)}</div>
                  </div>
                `;
              }).join("");

              // Compute per-column min/max for heatmap
              const heatCols = ["MeanNs", "ErrorNs", "StdDevNs", "P95Ns"];
              if (!allZeroAlloc) heatCols.push("AllocatedBytes");
              const heatRange = {};
              heatCols.forEach(k => {
                const vals = classBenchmarks.map(b => b[k] ?? 0).filter(v => v > 0);
                heatRange[k] = { min: Math.min(...vals), max: Math.min(Math.max(...vals), Math.min(...vals) * 3) };
              });

              const bestMeanNs = Math.min(...classBenchmarks.map(b => b.MeanNs));
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
                  const ratio = heatmapRatio(clamped, r.min, r.max);
                  return ratio >= 0 ? ` data-heatmap data-heat-ratio="${ratio.toFixed(4)}"` : "";
                };

                const opsHighlight = b.OpsPerSec === maxOps ? ' style="color: oklch(0.72 0.19 142);"' : "";

                return `
                  <tr data-params="${b.Parameters || ""}"${isBest ? ' class="best-row"' : ""}>
                    <td class="font-mono">${benchLabel(b)}</td>
                    <td class="text-right font-mono"${hm("MeanNs")}>${formatNs(b.MeanNs)}</td>
                    <td class="text-right font-mono"${opsHighlight}>${formatOps(b.OpsPerSec)}</td>
                    <td class="text-right font-mono"${hm("ErrorNs")}>${formatNs(b.ErrorNs)}</td>
                    <td class="text-right font-mono"${hm("StdDevNs")}>${formatNs(b.StdDevNs)}</td>
                    <td class="text-right font-mono"${hm("P95Ns")}>${formatNs(b.P95Ns)}</td>
                    <td class="text-right font-mono">${rankBadge(b.Rank)}</td>
                    ${!allZeroAlloc ? `<td class="text-right font-mono"${hm("AllocatedBytes")}>${allocBadge(b.AllocatedBytes)}</td>` : ""}
                    ${!allZeroGen0 ? `<td class="text-right font-mono">${formatGen(b.Gen0)}</td>` : ""}
                    ${!allZeroGen1 ? `<td class="text-right font-mono">${formatGen(b.Gen1)}</td>` : ""}
                    ${!allZeroGen2 ? `<td class="text-right font-mono">${formatGen(b.Gen2)}</td>` : ""}
                    ${deltaCell}
                  </tr>
                `;
              }).join("");

              const deltaHeader = isComparison ? `<th class="text-right" data-sort="DeltaPercent">Delta <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : "";

              // Parameter filters
              let filterHtml = "";
              if (hasParams) {
                const filterItems = params.map(p => {
                  const label = p.includes("=") ? p.split("=").slice(1).join("=") : p;
                  return `<label class="label cursor-pointer justify-start gap-2 py-1">
                    <input type="checkbox" class="toggle toggle-xs toggle-primary" data-filter="${p}" data-section="${sectionId}" checked />
                    <span class="text-xs font-mono">${label}</span>
                  </label>`;
                });

                filterHtml = `
                  <div class="collapse collapse-arrow bg-base-200 border border-base-300 mb-4 rounded-lg">
                    <input type="checkbox" class="peer" />
                    <div class="collapse-title flex items-center gap-2 text-xs font-semibold min-h-0 py-2 px-3">
                      <i data-lucide="filter" class="w-3.5 h-3.5"></i>
                      Scenarios
                      <span class="badge badge-xs badge-primary filter-count" data-section="${sectionId}">all</span>
                    </div>
                    <div class="collapse-content px-3 pb-3">
                      <label class="label cursor-pointer justify-start gap-2 py-1.5 border-b border-base-300 mb-1">
                        <input type="checkbox" class="toggle toggle-xs toggle-primary" data-filter-all="${sectionId}" checked />
                        <span class="text-xs font-semibold">Select all</span>
                      </label>
                      <div class="flex flex-wrap gap-x-4 gap-y-0.5 mt-1">
                        ${filterItems.join("")}
                      </div>
                    </div>
                  </div>
                `;
              }

              const sectionHtml = `
                <div class="class-section mb-8" id="${sectionId}">
                  <div class="flex items-center gap-3 mb-3 cursor-pointer select-none" onclick="toggleSection('${sectionId}')">
                    <i data-lucide="chevron-down" class="w-4 h-4 text-base-content/40 transition-transform" id="toggle-${sectionId}"></i>
                    <h2 class="text-lg font-semibold">${shortName}</h2>
                    <span class="text-base-content/40 text-xs">${classBenchmarks.length} benchmarks</span>
                  </div>
                  <div id="body-${sectionId}">
                    ${filterHtml}
                    <div class="card card-xs bg-base-200 border border-base-300 mb-3">
                      <div class="card-body">
                        <div class="flex flex-wrap gap-1.5 mb-4" data-section="${sectionId}">
                          ${metrics.filter(m => classBenchmarks.some(b => (b[m.key] ?? 0) !== 0)).map((m, i) => `<button class="btn btn-xs ${i === 0 ? 'btn-primary' : 'btn-ghost'}" data-metric="${m.key}" data-section="${sectionId}">${m.label}</button>`).join("")}
                        </div>
                        <div class="flex flex-col gap-2" id="chart-${sectionId}">${barRows}</div>
                      </div>
                    </div>
                    <div class="rounded-lg border border-base-300 overflow-x-auto mb-3">
                      <table class="table table-xs table-zebra col-hover">
                        <colgroup>
                          <col /><col /><col /><col /><col /><col /><col />
                          ${!allZeroAlloc ? "<col />" : ""}
                          ${!allZeroGen0 ? "<col />" : ""}
                          ${!allZeroGen1 ? "<col />" : ""}
                          ${!allZeroGen2 ? "<col />" : ""}
                          ${isComparison ? "<col />" : ""}
                        </colgroup>
                        <thead>
                          <tr>
                            <th data-sort="Method">Benchmark <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="MeanNs">Mean <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="OpsPerSec">Ops/sec <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="ErrorNs">Error <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="StdDevNs">StdDev <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="P95Ns">P95 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            <th class="text-right" data-sort="Rank">Rank <i data-lucide="arrow-up-down" class="sort-icon"></i></th>
                            ${!allZeroAlloc ? `<th class="text-right" data-sort="AllocatedBytes">Allocated <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen0 ? `<th class="text-right" data-sort="Gen0">Gen0 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen1 ? `<th class="text-right" data-sort="Gen1">Gen1 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${!allZeroGen2 ? `<th class="text-right" data-sort="Gen2">Gen2 <i data-lucide="arrow-up-down" class="sort-icon"></i></th>` : ""}
                            ${deltaHeader}
                          </tr>
                        </thead>
                        <tbody id="table-${sectionId}">${tableRows}</tbody>
                      </table>
                    </div>
                  </div>
                </div>
              `;

              classSections.insertAdjacentHTML("beforeend", sectionHtml);
            });

            // ── Chart metric switching ──────────────────────────────────────────

            function rebuildChart(sectionId, metricKey) {
              const cls = sectionId.replace("section-", "");
              const sectionBenchmarks = benchmarks.filter(b => b.Type === cls);
              const metric = metrics.find(m => m.key === metricKey);
              if (!metric) return;

              const values = sectionBenchmarks.map(b => b[metricKey] ?? 0);
              const maxVal = Math.max(...values, 1); // avoid division by zero

              const chart = document.getElementById("chart-" + sectionId);
              chart.querySelectorAll("[data-key]").forEach(row => {
                const key = row.dataset.key;
                const b = sectionBenchmarks.find(b => b.CompositeKey === key);
                if (!b) return;

                const val = b[metricKey] ?? 0;
                const pct = (val / maxVal) * 100;
                const fill = row.querySelector(".bar-fill");
                const valueEl = row.querySelector(".bar-value");

                if (fill) fill.style.width = pct + "%";
                if (valueEl) valueEl.textContent = metric.format(val);
              });
            }

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
                rebuildChart(sectionId, btn.dataset.metric);
              });
            });

            // ── Collapsible sections ────────────────────────────────────────────

            function toggleSection(sectionId) {
              const body = document.getElementById("body-" + sectionId);
              const toggle = document.getElementById("toggle-" + sectionId);
              body.classList.toggle("collapsed-body");
              toggle.classList.toggle("collapsed-toggle");
            }

            // ── Parameter filters (multi-select) ────────────────────────────────

            function applyFilters(sectionId) {
              const allCb = document.querySelector(`[data-filter-all="${sectionId}"]`);
              if (!allCb) return;
              const checkboxes = [...document.querySelectorAll(`[data-filter][data-section="${sectionId}"]`)];
              const section = document.getElementById(sectionId);

              const isAll = allCb.checked && !allCb.indeterminate;
              const activeParams = new Set(checkboxes.filter(c => c.checked).map(c => c.dataset.filter));

              const badge = document.querySelector(`.filter-count[data-section="${sectionId}"]`);
              if (badge) badge.textContent = isAll ? "all" : `${activeParams.size}/${checkboxes.length}`;

              section.querySelectorAll("[data-params]").forEach(el => {
                if (el.tagName === "TABLE" || el.tagName === "THEAD") return;
                el.style.display = (isAll || activeParams.has(el.dataset.params)) ? "" : "none";
              });
            }

            document.querySelectorAll("[data-filter-all]").forEach(allCb => {
              allCb.addEventListener("change", () => {
                const sectionId = allCb.dataset.filterAll;
                document.querySelectorAll(`[data-filter][data-section="${sectionId}"]`).forEach(cb => { cb.checked = allCb.checked; });
                applyFilters(sectionId);
              });
            });

            document.querySelectorAll("[data-filter][data-section]").forEach(cb => {
              cb.addEventListener("change", () => {
                const sectionId = cb.dataset.section;
                const allCb = document.querySelector(`[data-filter-all="${sectionId}"]`);
                const checkboxes = [...document.querySelectorAll(`[data-filter][data-section="${sectionId}"]`)];
                const allChecked = checkboxes.every(c => c.checked);
                const noneChecked = checkboxes.every(c => !c.checked);
                allCb.checked = allChecked;
                allCb.indeterminate = !allChecked && !noneChecked;
                if (noneChecked) {
                  allCb.checked = true;
                  checkboxes.forEach(c => { c.checked = true; });
                }
                applyFilters(sectionId);
              });
            });

            // ── Sortable tables ─────────────────────────────────────────────────

            document.querySelectorAll("thead th[data-sort]").forEach(th => {
              th.addEventListener("click", () => {
                const table   = th.closest("table");
                const tbody   = table.querySelector("tbody");
                const key     = th.dataset.sort;
                const isAsc   = th.classList.contains("sort-asc");

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

                // Find the section's benchmark data
                const sectionEl  = table.closest(".class-section");
                const cls        = sectionEl.id.replace("section-", "");
                const sectionBenchmarks = benchmarks.filter(b => b.Type === cls);

                // Sort benchmarks by the selected key
                const sorted = [...sectionBenchmarks].sort((a, b) => {
                  const av = a[key] ?? 0;
                  const bv = b[key] ?? 0;
                  return typeof av === "string"
                    ? dir * av.localeCompare(bv)
                    : dir * (av - bv);
                });

                // Reorder DOM rows to match sorted order
                const rows = [...tbody.querySelectorAll("tr[data-params]")];
                sorted.forEach(b => {
                  const label = b.Parameters ? `${b.Method}(${b.Parameters})` : b.Method;
                  const row = rows.find(r => {
                    return (r.querySelector("td:first-child")?.textContent ?? "") === label;
                  });
                  if (row) tbody.appendChild(row);
                });
              });
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

            // ── Theme toggle ────────────────────────────────────────────────────

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
                const lighten = isDark ? 0.55 : 0.25;
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
                document.body.style.fontSize = fontSize + "px";
                zoomLabel.textContent = fontSize;
                localStorage.setItem("bdr-zoom", fontSize);
              }
            })();

            lucide.createIcons();
            </script>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
