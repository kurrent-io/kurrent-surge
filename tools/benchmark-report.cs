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
            <html lang="en" class="dark">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Kurrent.Client Benchmark Report</title>
            <script src="https://cdn.tailwindcss.com"></script>
            <script>
            tailwindcss.config = {
              darkMode: 'class',
              theme: {
                extend: {
                  fontFamily: {
                    mono: ["'SF Mono'", "'Fira Code'", "'Cascadia Code'", "monospace"],
                  }
                }
              }
            }
            </script>
            <style>
              html { scroll-behavior: smooth; }

              .pill-active { background: #2563eb; border-color: #2563eb; color: white; }
              .dark .pill-active { background: #60a5fa; border-color: #60a5fa; color: #030712; }

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
              th[data-sort]:hover { color: #6b7280; }
              .dark th[data-sort]:hover { color: #d1d5db; }
              th[data-sort]::after { content: " \2195"; opacity: 0.3; font-size: 0.7rem; }
              th[data-sort].sort-asc::after { content: " \2191"; opacity: 1; }
              th[data-sort].sort-desc::after { content: " \2193"; opacity: 1; }
            </style>
            </head>
            <body class="bg-white dark:bg-gray-950 text-gray-900 dark:text-gray-100 leading-relaxed p-8 max-w-[1400px] mx-auto">

            <header class="flex justify-between items-start mb-6 pb-5 border-b border-gray-200 dark:border-gray-800">
              <div>
                <h1 class="text-xl font-semibold text-gray-900 dark:text-gray-100">Kurrent.Client Benchmark Report
                  <span class="inline-block px-2 py-0.5 rounded-full text-xs font-semibold ml-2 {{(data.IsComparison ? "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400" : "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400")}}">
                    {{(data.IsComparison ? "comparison" : "single-run")}}
                  </span>
                </h1>
                <p class="text-gray-500 dark:text-gray-400 text-sm">Generated {{timestamp}}</p>
              </div>
              <button class="border border-gray-200 dark:border-gray-700 rounded-lg text-gray-500 dark:text-gray-400 cursor-pointer text-base leading-none px-2.5 py-1.5 transition-colors hover:border-blue-500 hover:text-blue-500 dark:hover:border-blue-400 dark:hover:text-blue-400" id="theme-toggle" title="Toggle theme">&#9790;</button>
            </header>

            <div id="env-section"></div>
            <div id="stats-row"></div>
            <div id="highlights-section"></div>
            <div id="search-row"></div>
            <div class="flex flex-wrap gap-2 mb-6" id="section-nav"></div>
            <div id="regression-section"></div>
            <div id="class-sections"></div>

            <footer class="text-center text-gray-500 dark:text-gray-400 text-xs pt-4 border-t border-gray-200 dark:border-gray-800">
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
                case "Regressed": return "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400";
                case "Improved":  return "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400";
                case "New":       return "bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-400";
                default:          return "bg-gray-100 text-gray-500 dark:bg-gray-800/30 dark:text-gray-400";
              }
            }

            function rankBadge(rank) {
              const colors = {
                1: "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400",
                2: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400",
                3: "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400",
              };
              const cls = colors[rank] || "";
              return `<span class="inline-flex items-center justify-center w-5.5 h-5.5 rounded-full text-[0.7rem] font-semibold ${cls}">${rank}</span>`;
            }

            function allocBadge(bytes) {
              const cls = bytes === 0
                ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400"
                : "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400";
              return `<span class="inline-block px-2 py-0.5 rounded-full text-[0.7rem] font-semibold ${cls}">${formatBytes(bytes)}</span>`;
            }

            function benchLabel(b) {
              return b.Parameters ? `${b.Method}(${b.Parameters})` : b.Method;
            }

            function formatOps(ops) {
              if (ops >= 1_000_000_000) return (ops / 1_000_000_000).toFixed(2) + " Gops/s";
              if (ops >= 1_000_000)     return (ops / 1_000_000).toFixed(2)     + " Mops/s";
              if (ops >= 1_000)         return (ops / 1_000).toFixed(2)         + " Kops/s";
              return ops.toFixed(0) + " ops/s";
            }

            function formatGen(val) {
              return val === 0 ? `<span class="text-gray-400 dark:text-gray-500">\u2014</span>` : String(val);
            }

            // ── Color palette for bars ──────────────────────────────────────────

            const barColors = ["#3b82f6", "#22c55e", "#a78bfa", "#f59e0b", "#38bdf8", "#ef4444"];

            function barColor(index) {
              return barColors[index % barColors.length];
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
              const items = [];
              if (env.OsVersion) items.push({ label: "OS", value: env.OsVersion });
              if (env.ProcessorName) items.push({ label: "CPU", value: env.ProcessorName });
              if (env.LogicalCoreCount) items.push({
                label: "Cores",
                value: `${env.PhysicalCoreCount || "?"} physical, ${env.LogicalCoreCount} logical`
              });
              if (env.RuntimeVersion) items.push({ label: "Runtime", value: env.RuntimeVersion });
              if (env.Architecture) items.push({ label: "Architecture", value: env.Architecture });
              if (env.BenchmarkDotNetVersion) items.push({ label: "BenchmarkDotNet", value: env.BenchmarkDotNetVersion });
              if (jobConfig) {
                if (jobConfig.Toolchain) items.push({ label: "Toolchain", value: jobConfig.Toolchain });
                if (jobConfig.WarmupCount != null) items.push({ label: "Warmup", value: jobConfig.WarmupCount + " iterations" });
                if (jobConfig.IterationCount != null) items.push({ label: "Iterations", value: jobConfig.IterationCount + " iterations" });
              }
              if (env.Configuration) items.push({ label: "Configuration", value: env.Configuration });

              document.getElementById("env-section").innerHTML = `
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 px-4 py-3 mb-6">
                  <div class="flex flex-wrap gap-x-6 gap-y-1 text-sm">
                    ${items.map(i => `
                      <div>
                        <span class="text-gray-400 dark:text-gray-500 text-xs uppercase tracking-wide mr-1.5">${i.label}</span>
                        <span class="font-mono text-gray-900 dark:text-gray-100">${i.value}</span>
                      </div>
                    `).join("")}
                  </div>
                </div>
              `;
            }

            // ── Stats row ───────────────────────────────────────────────────────

            {
              const means = benchmarks.map(b => b.MeanNs);
              const fastest = Math.min(...means);
              const slowest = Math.max(...means);
              const totalAlloc = benchmarks.reduce((s, b) => s + b.AllocatedBytes, 0);

              let cards = `
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                  <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Benchmarks</div>
                  <div class="text-2xl font-semibold font-mono text-gray-900 dark:text-gray-100">${benchmarks.length}</div>
                </div>
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                  <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Fastest</div>
                  <div class="text-2xl font-semibold font-mono text-green-700 dark:text-green-400">${formatNs(fastest)}</div>
                </div>
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                  <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Slowest</div>
                  <div class="text-2xl font-semibold font-mono text-amber-600 dark:text-amber-400">${formatNs(slowest)}</div>
                </div>
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                  <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Total Allocations</div>
                  <div class="text-2xl font-semibold font-mono text-gray-900 dark:text-gray-100">${formatBytes(totalAlloc)}</div>
                </div>
              `;

              if (isComparison && comparisons) {
                const regressions = comparisons.filter(c => c.Classification === "Regressed").length;
                const improvements = comparisons.filter(c => c.Classification === "Improved").length;
                cards += `
                  <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                    <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Regressions</div>
                    <div class="text-2xl font-semibold font-mono ${regressions > 0 ? 'text-red-600 dark:text-red-400' : 'text-green-700 dark:text-green-400'}">${regressions}</div>
                  </div>
                  <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-4">
                    <div class="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Improvements</div>
                    <div class="text-2xl font-semibold font-mono ${improvements > 0 ? 'text-green-700 dark:text-green-400' : 'text-gray-500 dark:text-gray-400'}">${improvements}</div>
                  </div>
                `;
              }

              document.getElementById("stats-row").innerHTML = `
                <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-3 mb-6">${cards}</div>
              `;
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
                    <span class="font-mono text-xs text-gray-500 dark:text-gray-400 truncate min-w-0" title="${benchLabel(b)}">${benchLabel(b)}</span>
                    <span class="font-mono text-sm font-semibold whitespace-nowrap ${valueColor}">${valueGetter(b)}</span>
                  </div>
                `).join("");
              }

              const fastestCard = `
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
                  <div class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide border-b border-gray-200 dark:border-gray-800 text-green-700 dark:text-green-400">Top 5 Fastest</div>
                  <div class="divide-y divide-gray-100 dark:divide-gray-800">
                    ${highlightRows(fastest5, b => formatNs(b.MeanNs), "text-green-700 dark:text-green-400")}
                  </div>
                </div>`;

              const slowestCard = `
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
                  <div class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide border-b border-gray-200 dark:border-gray-800 text-red-600 dark:text-red-400">Top 5 Slowest</div>
                  <div class="divide-y divide-gray-100 dark:divide-gray-800">
                    ${highlightRows(slowest5, b => formatNs(b.MeanNs), "text-red-600 dark:text-red-400")}
                  </div>
                </div>`;

              const memoryRows = memory5.length > 0
                ? highlightRows(memory5, b => formatBytes(b.AllocatedBytes), "text-amber-600 dark:text-amber-400")
                : `<div class="flex items-center px-4 py-2"><span class="font-mono text-xs text-green-700 dark:text-green-400">All benchmarks allocate zero</span></div>`;

              const memoryCard = `
                <div class="rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
                  <div class="px-4 py-2.5 text-xs font-semibold uppercase tracking-wide border-b border-gray-200 dark:border-gray-800 text-amber-600 dark:text-amber-400">Top 5 Most Memory</div>
                  <div class="divide-y divide-gray-100 dark:divide-gray-800">
                    ${memoryRows}
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
              return `<a class="border border-gray-200 dark:border-gray-800 text-gray-500 dark:text-gray-400 px-3 py-1 rounded-full text-xs hover:border-blue-500 dark:hover:border-blue-400 hover:text-blue-600 dark:hover:text-blue-400 transition-colors no-underline" href="#section-${c}">${shortName}</a>`;
            }).join("");

            // ── Global search ───────────────────────────────────────────────────

            document.getElementById("search-row").innerHTML = `
              <input class="w-full rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 text-gray-900 dark:text-gray-100 text-sm px-4 py-2.5 outline-none focus:border-blue-500 dark:focus:border-blue-400 placeholder:text-gray-400 dark:placeholder:text-gray-500 mb-4" id="global-search" type="search"
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
                    <tr class="border-b border-gray-100 dark:border-gray-800/50 hover:bg-blue-50/50 dark:hover:bg-blue-900/5">
                      <td class="px-4 py-2.5 font-mono text-xs">${benchLabel(c.Current)}</td>
                      <td class="px-4 py-2.5 font-mono text-xs text-right">${formatNs(c.Baseline?.MeanNs ?? 0)}</td>
                      <td class="px-4 py-2.5 font-mono text-xs text-right">${formatNs(c.Current.MeanNs)}</td>
                      <td class="px-4 py-2.5 font-mono text-xs text-right"><span class="inline-block px-2 py-0.5 rounded-full text-[0.7rem] font-semibold bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400">${formatDelta(c.DeltaPercent)}</span></td>
                    </tr>
                  `).join("");

                document.getElementById("regression-section").innerHTML = `
                  <div class="rounded-lg border border-red-300 dark:border-red-800 overflow-hidden mb-6">
                    <h2 class="text-base font-semibold px-5 pt-4 pb-3 text-red-600 dark:text-red-400">Regressions (>${threshold}% threshold)</h2>
                    <table class="w-full text-sm border-collapse">
                      <thead>
                        <tr class="bg-gray-100 dark:bg-black/20">
                          <th class="text-left px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800">Benchmark</th>
                          <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800">Baseline</th>
                          <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800">Current</th>
                          <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800">Delta</th>
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

              // Bar chart
              const barRows = classBenchmarks.map((b, i) => {
                const pct = (b.MeanNs / barMax) * 100;
                const color = barColor(i);
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
                    <div class="bar-label font-mono text-xs text-gray-500 dark:text-gray-400 text-right whitespace-nowrap overflow-hidden text-ellipsis" title="${benchLabel(b)}">${benchLabel(b)}</div>
                    <div class="h-6 rounded bg-gray-100 dark:bg-gray-800/50 relative overflow-hidden">
                      ${baselineBar}
                      <div class="bar-fill h-full rounded opacity-80" style="width: ${pct}%; background: ${color};"></div>
                    </div>
                    <div class="bar-value font-mono text-sm text-right whitespace-nowrap" style="color: ${color}">${formatNs(b.MeanNs)}</div>
                  </div>
                `;
              }).join("");

              // Table rows
              const tableRows = classBenchmarks.map(b => {
                let deltaCell = "";
                if (isComparison) {
                  const comp = compMap[b.CompositeKey];
                  if (comp) {
                    const cls2 = deltaClass(comp.Classification);
                    const label = comp.Classification === "New" ? "new" : formatDelta(comp.DeltaPercent);
                    deltaCell = `<td class="px-4 py-2.5 font-mono text-xs text-right"><span class="inline-block px-2 py-0.5 rounded-full text-[0.7rem] font-semibold ${cls2}">${label}</span></td>`;
                  } else {
                    deltaCell = `<td class="px-4 py-2.5 font-mono text-xs text-right">\u2014</td>`;
                  }
                }

                return `
                  <tr class="border-b border-gray-100 dark:border-gray-800/50 hover:bg-blue-50/50 dark:hover:bg-blue-900/5" data-params="${b.Parameters || ""}">
                    <td class="px-4 py-2.5 font-mono text-xs">${benchLabel(b)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatNs(b.MeanNs)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatOps(b.OpsPerSec)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right text-gray-500 dark:text-gray-400">${formatNs(b.ErrorNs)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right text-gray-500 dark:text-gray-400">${formatNs(b.StdDevNs)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatNs(b.P95Ns)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${rankBadge(b.Rank)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${allocBadge(b.AllocatedBytes)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatGen(b.Gen0)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatGen(b.Gen1)}</td>
                    <td class="px-4 py-2.5 font-mono text-xs text-right">${formatGen(b.Gen2)}</td>
                    ${deltaCell}
                  </tr>
                `;
              }).join("");

              const deltaHeader = isComparison ? `<th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="DeltaPercent">Delta</th>` : "";

              // Parameter filters
              let filterHtml = "";
              if (hasParams) {
                const filterBtns = [`<button class="border border-gray-200 dark:border-gray-800 text-gray-500 dark:text-gray-400 px-3 py-1 rounded-full text-xs cursor-pointer transition-colors hover:border-blue-500 hover:text-blue-500 pill-active" data-filter="all" data-section="${sectionId}">all</button>`];
                params.forEach(p => {
                  const label = p.includes("=") ? p.split("=").slice(1).join("=") : p;
                  filterBtns.push(`<button class="border border-gray-200 dark:border-gray-800 text-gray-500 dark:text-gray-400 px-3 py-1 rounded-full text-xs cursor-pointer transition-colors hover:border-blue-500 hover:text-blue-500" data-filter="${p}" data-section="${sectionId}">${label}</button>`);
                });
                filterHtml = `<div class="flex flex-wrap gap-2 mb-4">${filterBtns.join("")}</div>`;
              }

              const sectionHtml = `
                <div class="class-section mb-8" id="${sectionId}">
                  <div class="flex items-center gap-3 mb-3 cursor-pointer select-none" onclick="toggleSection('${sectionId}')">
                    <span class="text-gray-400 text-xs transition-transform" id="toggle-${sectionId}">\u25bc</span>
                    <h2 class="text-lg font-semibold text-gray-900 dark:text-gray-100">${shortName}</h2>
                    <span class="text-gray-500 dark:text-gray-400 text-xs">${classBenchmarks.length} benchmarks</span>
                  </div>
                  <div id="body-${sectionId}">
                    ${filterHtml}
                    <div class="rounded-lg border border-gray-200 dark:border-gray-800 bg-gray-50 dark:bg-gray-900 p-5 mb-3">
                      <div class="flex flex-wrap gap-1.5 mb-4" data-section="${sectionId}">
                        ${metrics.filter(m => classBenchmarks.some(b => (b[m.key] ?? 0) !== 0)).map((m, i) => `<button class="border border-gray-200 dark:border-gray-800 text-gray-500 dark:text-gray-400 px-3 py-1 rounded-full text-xs cursor-pointer transition-colors hover:border-blue-500 hover:text-blue-500${i === 0 ? ' pill-active' : ''}" data-metric="${m.key}" data-section="${sectionId}">${m.label}</button>`).join("")}
                      </div>
                      <div class="flex flex-col gap-2" id="chart-${sectionId}">${barRows}</div>
                    </div>
                    <div class="rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden mb-3">
                      <table class="w-full text-sm border-collapse">
                        <thead>
                          <tr class="bg-gray-100 dark:bg-black/20">
                            <th class="text-left px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="Method">Benchmark</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="MeanNs">Mean</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="OpsPerSec">Ops/sec</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="ErrorNs">Error</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="StdDevNs">StdDev</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="P95Ns">P95</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="Rank">Rank</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="AllocatedBytes">Allocated</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="Gen0">Gen0</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="Gen1">Gen1</th>
                            <th class="text-right px-4 py-2.5 text-gray-500 dark:text-gray-400 font-medium text-xs uppercase tracking-wide border-b border-gray-200 dark:border-gray-800" data-sort="Gen2">Gen2</th>
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

            document.querySelectorAll("[data-section] [data-metric]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.section;
                // Update active state within this row
                btn.closest("[data-section]").querySelectorAll("[data-metric]").forEach(b => b.classList.remove("pill-active"));
                btn.classList.add("pill-active");
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

            document.querySelectorAll("[data-filter]").forEach(btn => {
              btn.addEventListener("click", () => {
                const filterRow = btn.parentElement;
                const sectionId = btn.dataset.section;
                const filter    = btn.dataset.filter;
                const allBtn    = filterRow.querySelector('[data-filter="all"]');
                const paramBtns = [...filterRow.querySelectorAll('[data-filter]:not([data-filter="all"])')];

                if (filter === "all") {
                  // "All" resets: activate all, deactivate individual pills
                  allBtn.classList.add("pill-active");
                  paramBtns.forEach(b => b.classList.remove("pill-active"));
                } else {
                  // Toggle this pill
                  btn.classList.toggle("pill-active");
                  // Deactivate "all" when any individual is toggled
                  allBtn.classList.remove("pill-active");
                  // If no individual pills are active, reactivate "all"
                  const anyActive = paramBtns.some(b => b.classList.contains("pill-active"));
                  if (!anyActive) allBtn.classList.add("pill-active");
                }

                // Determine which params are visible
                const isAll = allBtn.classList.contains("pill-active");
                const activeParams = new Set(
                  paramBtns.filter(b => b.classList.contains("pill-active")).map(b => b.dataset.filter)
                );

                // Apply filter to bars and table rows
                const section = document.getElementById(sectionId);
                section.querySelectorAll("[data-params]").forEach(el => {
                  if (el.tagName === "TABLE" || el.tagName === "THEAD") return;
                  const show = isAll || activeParams.has(el.dataset.params);
                  el.style.display = show ? "" : "none";
                });
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
                });

                const dir = isAsc ? -1 : 1;
                th.classList.add(isAsc ? "sort-desc" : "sort-asc");

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

            // ── URL hash navigation ─────────────────────────────────────────────

            if (location.hash) {
              const target = document.querySelector(location.hash);
              if (target) {
                setTimeout(() => target.scrollIntoView({ behavior: "smooth" }), 100);
              }
            }

            // ── Theme toggle ────────────────────────────────────────────────────

            (function() {
              const root   = document.documentElement;
              const btn    = document.getElementById("theme-toggle");
              const stored = localStorage.getItem("bdr-theme");

              function applyTheme(theme) {
                if (theme === "dark") {
                  root.classList.add("dark");
                } else {
                  root.classList.remove("dark");
                }
                btn.innerHTML = theme === "light" ? "&#9790;" : "&#9728;";
                localStorage.setItem("bdr-theme", theme);
              }

              applyTheme(stored ?? "dark");

              btn.addEventListener("click", () => {
                const isDark = root.classList.contains("dark");
                applyTheme(isDark ? "light" : "dark");
              });
            })();
            </script>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
