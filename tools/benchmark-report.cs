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

record MeasurementData(
    double[] Values,
    JsonElement? Percentiles
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
[JsonSerializable(typeof(Dictionary<string, MeasurementData>))]
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
        var (benchmarks, hostInfo, jobConfig, measurementMap) = ParseBenchmarkFiles(inputFiles);
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

            (baselineBenchmarks, _, _, _) = ParseBenchmarkFiles(baselineFiles);
            AnsiConsole.MarkupLine($"[grey]Baseline benchmarks:[/] [white]{baselineBenchmarks.Count}[/]");

            comparisons = BuildComparisons(benchmarks, baselineBenchmarks, settings.Threshold);
        }

        var reportData = new ReportData(hostInfo, jobConfig, benchmarks, comparisons, isComparison);

        // ── Generate HTML ───────────────────────────────────────────────────
        var html = GenerateHtml(reportData, measurementMap, settings.Threshold);
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

    static (List<BenchmarkEntry> Benchmarks, BdnHostInfo? HostInfo, JobConfig? JobConfig, Dictionary<string, MeasurementData> MeasurementMap)
        ParseBenchmarkFiles(List<string> files) {
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

        // Build measurement map from raw benchmarks
        var measurementMap = new Dictionary<string, MeasurementData>();
        foreach (var b in rawBenchmarks) {
            var type   = b.Type ?? "Unknown";
            var method = b.Method ?? "Unknown";
            var key    = b.Parameters is not null
                ? $"{type}.{method}|{b.Parameters}"
                : $"{type}.{method}";

            var values = b.Measurements?
                .Where(m => m.IterationMode == "Workload" && m.IterationStage == "Actual")
                .Select(m => m.Value)
                .ToArray() ?? [];

            measurementMap[key] = new MeasurementData(values, b.Statistics?.Percentiles);
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

        return (benchmarks, hostInfo, jobConfig, measurementMap);
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

    static string GenerateHtml(ReportData data, Dictionary<string, MeasurementData> measurementMap, double threshold) {
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
        var measurementsJson = JsonSerializer.Serialize(measurementMap, Ctx.DictionaryStringMeasurementData);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en" data-theme="dark">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Kurrent.Client Benchmark Report</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <script src="https://unpkg.com/lucide@latest/dist/umd/lucide.min.js"></script>
            <script src="https://cdn.jsdelivr.net/npm/chart.js@4"></script>
            <style>
            :root {
                --bg-root: #07090d;
                --bg-surface: #0c1017;
                --bg-elevated: #111820;
                --bg-hover: #161e2a;
                --border: #1c2536;
                --border-subtle: #151d2a;
                --text-primary: #e0e6ed;
                --text-secondary: #8494a7;
                --text-muted: #4f6178;
                --accent-green: #34d97a;
                --accent-green-glow: #34d97a40;
                --accent-red: #ff5c5c;
                --accent-red-glow: #ff5c5c40;
                --accent-amber: #ffb545;
                --accent-amber-glow: #ffb54540;
                --accent-blue: #5ea4ff;
                --accent-blue-glow: #5ea4ff40;
                --accent-purple: #a78bfa;
                --accent-purple-glow: #a78bfa40;
                --radius-card: 5px;
                --radius-sm: 5px;
                --font-mono: 'JetBrains Mono', 'SF Mono', 'Cascadia Code', monospace;
                --font-body: -apple-system, 'SF Pro Display', system-ui, sans-serif;
              }

              * { margin: 0; padding: 0; box-sizing: border-box; }

              body {
                background: var(--bg-root);
                color: var(--text-primary);
                font-family: var(--font-mono);
                font-size: 14px;
                line-height: 1.55;
                max-width: 1400px;
                margin: 0 auto;
                padding: 0 24px;
              }

              /* ── Sticky Header */
              .sticky-header {
                position: sticky;
                top: 0;
                z-index: 50;
                background: color-mix(in srgb, var(--bg-root) 92%, transparent);
                border-bottom: 1px solid var(--border);
                padding: 20px 0 14px;
                margin-bottom: 24px;
                backdrop-filter: blur(16px);
              }

              .header-top {
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 12px;
              }

              .header-title {
                display: flex;
                align-items: center;
                gap: 12px;
              }

              .header-title h1 {
                font-family: var(--font-body);
                font-size: 22px;
                font-weight: 700;
                letter-spacing: -0.3px;
              }

              .badge {
                font-size: 12px;
                font-weight: 600;
                padding: 3px 10px;
                border-radius: var(--radius-sm);
                text-transform: uppercase;
                letter-spacing: 0.8px;
              }
              .badge-mode {
                background: var(--accent-blue-glow);
                color: var(--accent-blue);
                border: 1px solid color-mix(in srgb, var(--accent-blue) 30%, transparent);
              }
              .badge-count {
                background: var(--bg-elevated);
                color: var(--text-secondary);
                border: 1px solid var(--border);
              }

              .header-meta {
                font-size: 13px;
                color: var(--text-muted);
              }

              .header-controls {
                display: flex;
                align-items: center;
                gap: 8px;
              }

              .icon-btn {
                display: flex;
                align-items: center;
                justify-content: center;
                width: 32px;
                height: 32px;
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                border-radius: var(--radius-sm);
                color: var(--text-secondary);
                cursor: pointer;
                transition: all 0.15s;
              }
              .icon-btn:hover { background: var(--bg-hover); color: var(--text-primary); }

              .header-row-2 {
                display: flex;
                align-items: center;
                gap: 10px;
              }

              .search-input {
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                border-radius: var(--radius-sm);
                padding: 7px 12px;
                color: var(--text-primary);
                font-family: var(--font-mono);
                font-size: 14px;
                outline: none;
                transition: border-color 0.15s;
              }
              .search-input:focus { border-color: var(--accent-blue); }
              .search-input::placeholder { color: var(--text-muted); }

              .search-box {
                position: relative;
                display: flex;
                align-items: center;
                flex: 1;
                min-width: 140px;
                transition: flex 0.25s ease;
              }
              .search-box .search-input {
                width: 100%;
                padding-right: 32px;
              }
              .search-box .kbd-inside {
                position: absolute;
                right: 10px;
                pointer-events: none;
                transition: opacity 0.15s;
              }
              .search-box .search-input:focus + .kbd-inside,
              .search-box .search-input:not(:placeholder-shown) + .kbd-inside { opacity: 0; }

              .jump-select {
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                border-radius: var(--radius-sm);
                padding: 7px 10px;
                color: var(--text-secondary);
                font-family: var(--font-mono);
                font-size: 13px;
                outline: none;
                cursor: pointer;
                width: auto;
                max-width: 200px;
              }

              .kbd {
                font-size: 11px;
                font-weight: 600;
                padding: 2px 5px;
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                border-radius: 3px;
                color: var(--text-muted);
              }

              /* ── Environment Panel */
              .env-panel {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 10px;
                margin-bottom: 24px;
              }

              .env-card {
                background: var(--bg-surface);
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                padding: 16px 20px;
              }

              .env-card-title {
                font-size: 11px;
                text-transform: uppercase;
                letter-spacing: 1.8px;
                color: var(--text-muted);
                margin-bottom: 12px;
                display: flex;
                align-items: center;
                gap: 6px;
              }

              .env-grid {
                display: grid;
                grid-template-columns: auto 1fr;
                gap: 8px 14px;
                font-size: 13px;
              }
              .env-grid .label { color: var(--text-muted); font-weight: 500; }
              .env-grid .value { color: var(--text-secondary); }

              /* ── Bento Stat Cards */
              .bento-grid {
                display: grid;
                grid-template-columns: 1fr 1fr 1fr 1fr;
                gap: 10px;
                margin-bottom: 20px;
              }

              .bento-card {
                background: var(--bg-surface);
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                padding: 18px 20px;
                transition: border-color 0.2s;
              }
              .bento-card:hover { border-color: var(--border); }

              .bento-label {
                font-size: 11px;
                text-transform: uppercase;
                letter-spacing: 1.8px;
                color: var(--text-muted);
                margin-bottom: 6px;
                display: flex;
                align-items: center;
                gap: 6px;
              }

              .bento-value {
                font-family: var(--font-body);
                font-size: 30px;
                font-weight: 800;
                letter-spacing: -1.5px;
                line-height: 1.1;
              }

              .bento-detail {
                font-size: 12px;
                color: var(--text-muted);
                margin-top: 4px;
              }

              .bento-value.green { color: var(--accent-green); text-shadow: 0 0 20px var(--accent-green-glow); }
              .bento-value.red { color: var(--accent-red); text-shadow: 0 0 20px var(--accent-red-glow); }
              .bento-value.amber { color: var(--accent-amber); text-shadow: 0 0 20px var(--accent-amber-glow); }
              .bento-value.blue { color: var(--accent-blue); text-shadow: 0 0 20px var(--accent-blue-glow); }

              /* ── Section Header */
              .section-header {
                display: flex;
                align-items: center;
                gap: 10px;
                margin-bottom: 14px;
                cursor: pointer;
                user-select: none;
              }

              .section-header h2 {
                font-family: var(--font-body);
                font-size: 19px;
                font-weight: 700;
                letter-spacing: -0.3px;
              }

              .section-count {
                font-size: 12px;
                color: var(--text-muted);
              }

              .status-dot {
                width: 7px;
                height: 7px;
                border-radius: 50%;
                background: var(--accent-green);
                box-shadow: 0 0 8px var(--accent-green-glow);
              }

              /* ── Group-by pill bar */
              .group-controls {
                display: flex;
                align-items: center;
                gap: 8px;
                margin-bottom: 12px;
                font-size: 12px;
              }
              .group-pill {
                padding: 4px 10px;
                border-radius: var(--radius-sm);
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                color: var(--text-secondary);
                cursor: pointer;
                font-family: var(--font-mono);
                font-size: 12px;
                font-weight: 600;
                transition: all 0.15s;
              }
              .group-pill:hover { border-color: var(--accent-blue); color: var(--text-primary); }
              .group-pill.active { background: var(--accent-blue-glow); border-color: var(--accent-blue); color: var(--accent-blue); }

              /* ── Data Table */
              .table-wrap {
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                overflow: hidden;
                margin-bottom: 16px;
              }

              table {
                width: 100%;
                border-collapse: collapse;
                font-size: 14px;
              }

              thead tr { background: var(--bg-elevated); }

              th {
                text-align: left;
                padding: 10px 14px;
                font-size: 11px;
                text-transform: uppercase;
                letter-spacing: 1.2px;
                color: var(--text-muted);
                border-bottom: 1px solid var(--border);
                font-weight: 600;
                white-space: nowrap;
                cursor: pointer;
                user-select: none;
                position: sticky;
                top: 0;
                background: var(--bg-elevated);
                z-index: 5;
                transition: color 0.15s;
              }
              th:hover { color: var(--text-primary); }
              th.text-right, td.text-right { text-align: right; }

              th {
                vertical-align: middle;
              }

              th .th-inner {
                display: inline-flex;
                align-items: center;
                gap: 3px;
                vertical-align: middle;
              }

              th.text-right .th-inner {
                float: right;
              }

              th .col-icon {
                width: 12px;
                height: 12px;
                flex-shrink: 0;
                opacity: 0.4;
                transition: opacity 0.15s;
              }
              th:hover .col-icon { opacity: 0.7; }
              th.sort-asc .col-icon, th.sort-desc .col-icon { opacity: 1; }

              th .sort-icon {
                width: 12px;
                height: 12px;
                flex-shrink: 0;
                opacity: 0;
                transition: opacity 0.15s, transform 0.15s;
              }
              th:hover .sort-icon { opacity: 0.3; }
              th.sort-asc .sort-icon, th.sort-desc .sort-icon { opacity: 1; color: var(--accent-blue); }
              th.sort-desc .sort-icon { transform: rotate(180deg); }

              th.sort-asc, th.sort-desc {
                color: var(--text-primary);
                background: color-mix(in srgb, var(--accent-blue) 8%, var(--bg-elevated));
              }

              td {
                padding: 8px 14px;
                border-bottom: 1px solid var(--border-subtle);
                white-space: nowrap;
              }
              tr:last-child td { border-bottom: none; }
              tr:hover td { background: var(--bg-hover); }

              .method-name { font-weight: 600; color: var(--text-primary); }
              .value-best { color: var(--accent-green); font-weight: 700; }
              .value-worst { color: var(--accent-red); font-weight: 600; }

              .sparkline {
                display: inline-flex;
                align-items: center;
                gap: 2px;
                height: 16px;
                vertical-align: middle;
              }
              .sparkline .bar {
                width: 2px;
                background: var(--accent-blue);
                border-radius: 1px;
                opacity: 0.6;
              }

              .rank {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                width: 24px;
                height: 24px;
                border-radius: var(--radius-sm);
                font-size: 12px;
                font-weight: 700;
              }
              .rank-1 { background: var(--accent-green-glow); color: var(--accent-green); }
              .rank-2 { background: var(--accent-blue-glow); color: var(--accent-blue); }
              .rank-3 { background: var(--accent-amber-glow); color: var(--accent-amber); }
              .rank-n { background: var(--bg-elevated); color: var(--text-muted); }

              .alloc-badge {
                font-size: 12px;
                padding: 3px 9px;
                border-radius: var(--radius-sm);
                font-weight: 600;
              }
              .alloc-zero { background: var(--bg-elevated); color: var(--text-muted); }
              .alloc-low { background: var(--accent-green-glow); color: var(--accent-green); }
              .alloc-mid { background: var(--accent-amber-glow); color: var(--accent-amber); }
              .alloc-high { background: var(--accent-red-glow); color: var(--accent-red); }

              tr.active td {
                background: var(--bg-hover);
                box-shadow: inset 3px 0 0 var(--accent-blue);
              }

              /* ── Charts Section */
              .charts-disclosure {
                margin-top: 16px;
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                background: var(--bg-surface);
                overflow: hidden;
              }

              .charts-toggle {
                display: flex;
                align-items: center;
                gap: 8px;
                padding: 12px 18px;
                cursor: pointer;
                user-select: none;
                transition: background 0.15s;
              }
              .charts-toggle:hover {
                background: var(--bg-hover);
              }
              .charts-disclosure:not(.collapsed) .charts-toggle {
                border-bottom: 1px solid var(--border-subtle);
              }

              .charts-toggle .chevron-icon {
                transition: transform 0.25s ease;
              }
              .charts-disclosure.collapsed .chevron-icon {
                transform: rotate(-90deg);
              }

              .charts-body {
                padding: 14px 18px 18px;
                max-height: 800px;
                opacity: 1;
                transition: max-height 0.35s ease, opacity 0.25s ease, padding 0.3s ease;
                overflow: hidden;
              }
              .charts-disclosure.collapsed .charts-body {
                max-height: 0;
                padding-top: 0;
                padding-bottom: 0;
                opacity: 0;
              }

              .charts-grid {
                display: grid;
                grid-template-columns: 3fr 2fr;
                gap: 10px;
                margin-bottom: 20px;
              }

              .chart-card {
                background: var(--bg-surface);
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                padding: 18px 20px;
              }

              .chart-header {
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 14px;
              }

              .chart-title {
                font-size: 11px;
                text-transform: uppercase;
                letter-spacing: 1.5px;
                color: var(--text-muted);
                display: flex;
                align-items: center;
                gap: 6px;
              }

              .metric-pills {
                display: flex;
                gap: 4px;
              }

              .metric-pill {
                padding: 3px 8px;
                border-radius: var(--radius-sm);
                font-size: 12px;
                font-weight: 600;
                font-family: var(--font-mono);
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                color: var(--text-muted);
                cursor: pointer;
                transition: all 0.15s;
              }
              .metric-pill:hover { color: var(--text-secondary); border-color: var(--accent-blue); }
              .metric-pill.active { background: var(--accent-blue-glow); border-color: var(--accent-blue); color: var(--accent-blue); }

              .chart-container {
                position: relative;
                height: 220px;
              }

              /* Distribution plot card */
              .dist-plot {
                display: flex;
                flex-direction: column;
                gap: 10px;
              }

              .dist-row {
                display: flex;
                align-items: center;
                gap: 10px;
                font-size: 12px;
              }

              .dist-label {
                width: 100px;
                text-align: right;
                color: var(--text-secondary);
                font-weight: 500;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
              }

              .dist-box {
                flex: 1;
                height: 22px;
                position: relative;
                display: flex;
                align-items: center;
              }

              .dist-whisker {
                position: absolute;
                height: 1px;
                background: var(--text-muted);
              }

              .dist-box-rect {
                position: absolute;
                height: 16px;
                border-radius: 3px;
                opacity: 0.7;
                top: 3px;
              }

              .dist-median {
                position: absolute;
                width: 2px;
                height: 22px;
                border-radius: 1px;
                top: 0;
              }

              .dist-value {
                font-size: 13px;
                color: var(--text-muted);
                width: 70px;
                text-align: right;
                white-space: nowrap;
              }

              /* ── Keyboard hints */
              .keyboard-hints {
                display: flex;
                gap: 16px;
                padding: 10px 16px;
                background: var(--bg-surface);
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-sm);
                margin-top: 20px;
                margin-bottom: 20px;
                font-size: 13px;
                color: var(--text-muted);
              }
              .keyboard-hints .hint {
                display: flex;
                align-items: center;
                gap: 6px;
              }

              /* ── Legends card */
              .legends-card {
                background: var(--bg-surface);
                border: 1px solid var(--border-subtle);
                border-radius: var(--radius-card);
                padding: 16px 20px;
                margin-bottom: 24px;
              }

              footer {
                text-align: center;
                padding: 24px 0;
                border-top: 1px solid var(--border-subtle);
                margin-top: 32px;
                font-size: 13px;
                color: var(--text-muted);
              }

              /* ── Table pagination */
              .table-pagination {
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 10px 16px;
                border-top: 1px solid var(--border-subtle);
                font-size: 13px;
              }

              .page-info {
                color: var(--text-muted);
              }

              .page-controls {
                display: flex;
                align-items: center;
                gap: 4px;
              }

              .page-btn {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                min-width: 32px;
                height: 30px;
                padding: 0 8px;
                border-radius: var(--radius-sm);
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                color: var(--text-secondary);
                font-family: var(--font-mono);
                font-size: 13px;
                font-weight: 600;
                cursor: pointer;
                transition: all 0.15s;
                user-select: none;
              }
              .page-btn:hover:not(.disabled):not(.active) {
                background: var(--bg-hover);
                border-color: var(--accent-blue);
                color: var(--text-primary);
                transform: translateY(-1px);
              }
              .page-btn:active:not(.disabled) { transform: translateY(0); }
              .page-btn.active {
                background: var(--accent-blue-glow);
                border-color: var(--accent-blue);
                color: var(--accent-blue);
              }
              .page-btn.disabled {
                opacity: 0.3;
                cursor: default;
              }

              .page-size-select {
                background: var(--bg-elevated);
                border: 1px solid var(--border);
                border-radius: var(--radius-sm);
                padding: 4px 8px;
                color: var(--text-secondary);
                font-family: var(--font-mono);
                font-size: 12px;
                outline: none;
                cursor: pointer;
                margin-left: 12px;
              }

              /* ── Entry animations */
              @keyframes fadeSlideUp {
                from { opacity: 0; transform: translateY(12px); }
                to   { opacity: 1; transform: translateY(0); }
              }

              @keyframes growRight {
                from { transform: scaleX(0); }
                to   { transform: scaleX(1); }
              }

              @keyframes fadeIn {
                from { opacity: 0; }
                to   { opacity: 1; }
              }

              @keyframes pulseGlow {
                0%, 100% { box-shadow: 0 0 8px var(--accent-green-glow); }
                50%      { box-shadow: 0 0 14px var(--accent-green-glow), 0 0 4px var(--accent-green); }
              }

              .bento-card {
                animation: fadeSlideUp 0.5s ease backwards;
              }
              .bento-card:nth-child(1) { animation-delay: 0.05s; }
              .bento-card:nth-child(2) { animation-delay: 0.12s; }
              .bento-card:nth-child(3) { animation-delay: 0.19s; }
              .bento-card:nth-child(4) { animation-delay: 0.26s; }

              .env-card {
                animation: fadeSlideUp 0.4s ease backwards;
              }
              .env-card:nth-child(1) { animation-delay: 0s; }
              .env-card:nth-child(2) { animation-delay: 0.08s; }

              .chart-card {
                animation: fadeSlideUp 0.5s ease backwards;
                animation-delay: 0.3s;
              }

              .table-wrap {
                animation: fadeIn 0.4s ease backwards;
                animation-delay: 0.45s;
              }

              .dist-whisker {
                transform-origin: left;
                animation: growRight 0.6s ease backwards;
              }
              .dist-box-rect {
                transform-origin: left;
                animation: growRight 0.7s ease backwards;
              }
              .dist-median {
                animation: fadeIn 0.3s ease backwards;
              }
              .dist-row:nth-child(1) .dist-whisker, .dist-row:nth-child(1) .dist-box-rect { animation-delay: 0.35s; }
              .dist-row:nth-child(1) .dist-median { animation-delay: 0.75s; }
              .dist-row:nth-child(2) .dist-whisker, .dist-row:nth-child(2) .dist-box-rect { animation-delay: 0.45s; }
              .dist-row:nth-child(2) .dist-median { animation-delay: 0.85s; }
              .dist-row:nth-child(3) .dist-whisker, .dist-row:nth-child(3) .dist-box-rect { animation-delay: 0.55s; }
              .dist-row:nth-child(3) .dist-median { animation-delay: 0.95s; }
              .dist-row:nth-child(4) .dist-whisker, .dist-row:nth-child(4) .dist-box-rect { animation-delay: 0.65s; }
              .dist-row:nth-child(4) .dist-median { animation-delay: 1.05s; }
              .dist-row:nth-child(5) .dist-whisker, .dist-row:nth-child(5) .dist-box-rect { animation-delay: 0.75s; }
              .dist-row:nth-child(5) .dist-median { animation-delay: 1.15s; }
              .dist-row:nth-child(6) .dist-whisker, .dist-row:nth-child(6) .dist-box-rect { animation-delay: 0.85s; }
              .dist-row:nth-child(6) .dist-median { animation-delay: 1.25s; }

              .status-dot {
                animation: pulseGlow 2.5s ease infinite;
              }

              .keyboard-hints {
                animation: fadeIn 0.4s ease backwards;
                animation-delay: 0.6s;
              }

              .section-header {
                animation: fadeSlideUp 0.4s ease backwards;
                animation-delay: 0.1s;
              }

              /* ── Hover animations */
              .bento-card {
                transition: border-color 0.2s, transform 0.2s, box-shadow 0.2s;
              }
              .bento-card:hover {
                border-color: var(--border);
                transform: translateY(-2px);
                box-shadow: 0 8px 24px rgba(0,0,0,0.15);
              }

              .env-card {
                transition: border-color 0.2s, transform 0.15s;
              }
              .env-card:hover {
                border-color: var(--border);
                transform: translateY(-1px);
              }

              .chart-card {
                transition: border-color 0.2s, box-shadow 0.2s;
              }
              .chart-card:hover {
                border-color: var(--border);
                box-shadow: 0 4px 16px rgba(0,0,0,0.12);
              }

              .icon-btn {
                transition: all 0.15s ease;
              }
              .icon-btn:hover {
                background: var(--bg-hover);
                color: var(--text-primary);
                transform: scale(1.08);
              }
              .icon-btn:active { transform: scale(0.95); }

              .group-pill {
                transition: all 0.15s ease;
              }
              .group-pill:hover {
                border-color: var(--accent-blue);
                color: var(--text-primary);
                transform: translateY(-1px);
              }
              .group-pill:active { transform: translateY(0); }

              .metric-pill {
                transition: all 0.15s ease;
              }
              .metric-pill:hover {
                color: var(--text-secondary);
                border-color: var(--accent-blue);
                transform: translateY(-1px);
              }

              .rank {
                transition: transform 0.15s ease;
              }
              tr:hover .rank { transform: scale(1.15); }

              .alloc-badge {
                transition: transform 0.15s ease;
              }
              tr:hover .alloc-badge { transform: scale(1.05); }

              .sparkline .bar {
                transition: height 0.2s ease, opacity 0.15s ease;
              }
              tr:hover .sparkline .bar { opacity: 1; }

              .section-header {
                transition: color 0.15s;
              }
              .section-header:hover .status-dot {
                box-shadow: 0 0 12px var(--accent-green-glow), 0 0 4px var(--accent-green);
              }
              .status-dot { transition: box-shadow 0.2s; }

              tr td {
                transition: background 0.1s ease, box-shadow 0.1s ease;
              }

              .search-input {
                transition: border-color 0.15s, box-shadow 0.2s;
              }
              .search-input:focus {
                border-color: var(--accent-blue);
                box-shadow: 0 0 0 3px var(--accent-blue-glow);
              }

              /* ── Light theme */
              [data-theme="light"] {
                --bg-root: #e8eaef;
                --bg-surface: #f0f1f5;
                --bg-elevated: #d9dce4;
                --bg-hover: #d0d4dd;
                --border: #b8bec9;
                --border-subtle: #ccd1da;
                --text-primary: #1a1d24;
                --text-secondary: #454d5e;
                --text-muted: #6e7788;
                --accent-green: #15803d;
                --accent-green-glow: #15803d25;
                --accent-red: #b91c1c;
                --accent-red-glow: #b91c1c25;
                --accent-amber: #b45309;
                --accent-amber-glow: #b4530925;
                --accent-blue: #1d4ed8;
                --accent-blue-glow: #1d4ed825;
                --accent-purple: #6d28d9;
                --accent-purple-glow: #6d28d925;
              }
            </style>
            </head>
            <body>

            <div class="sticky-header">
              <div class="header-top">
                <div class="header-title">
                  <h1>Kurrent.Client Benchmark Report</h1>
                  <span class="badge badge-mode">{{(data.IsComparison ? "comparison" : "single-run")}}</span>
                  <span class="badge badge-count" id="benchmark-count-badge"></span>
                  <span class="header-meta">Generated {{timestamp}}</span>
                </div>
                <div class="header-controls">
                  <div class="icon-btn" title="Toggle theme" onclick="toggleTheme()">
                    <i data-lucide="sun-moon" style="width: 15px; height: 15px;"></i>
                  </div>
                  <div class="icon-btn" id="toggle-all" title="Expand/Collapse all" onclick="toggleAllSections()">
                    <i data-lucide="unfold-vertical" style="width: 15px; height: 15px;"></i>
                  </div>
                </div>
              </div>
              <div class="header-row-2">
                <div class="search-box">
                  <input class="search-input" type="search" id="global-search"
                         placeholder="Search benchmarks..." oninput="filterSearch(this.value)">
                  <span class="kbd kbd-inside">/</span>
                </div>
                <select class="jump-select" id="jump-select" onchange="jumpToSection(this.value)">
                  <option value="">Jump to...</option>
                </select>
              </div>
            </div>

            <!-- Environment Panel -->
            <div class="env-panel" id="env-section"></div>

            <!-- Bento Stat Cards -->
            <div class="bento-grid" id="bento-stats"></div>

            <!-- Regression Summary (comparison mode only) -->
            <div id="regression-section" style="display:none;"></div>

            <!-- Benchmark Sections (one per class) -->
            <div id="class-sections"></div>

            <!-- Keyboard hints -->
            <div class="keyboard-hints">
              <span class="hint"><span class="kbd">j</span><span class="kbd">k</span> navigate rows</span>
              <span class="hint"><span class="kbd">/</span> search</span>
              <span class="hint"><span class="kbd">Enter</span> expand</span>
              <span class="hint"><span class="kbd">Esc</span> collapse</span>
            </div>

            <!-- Legends -->
            <div class="legends-card" id="legends-section"></div>

            <!-- Footer -->
            <footer>
              <code>Kurrent.Client</code> &middot; Generated by benchmark-report
            </footer>

            <script>
            // ── Data (injected by C# template) ──────────────────────────────────
            const benchmarks = {{benchmarkJson}};
            const comparisons = {{comparisonJson}};
            const env = {{envJson}};
            const jobConfig = {{jobConfigJson}};
            const classes = {{classesJson}};
            const measurements = {{measurementsJson}};
            const isComparison = {{(data.IsComparison ? "true" : "false")}};
            const threshold = {{threshold}};

            // ── Formatting Functions ─────────────────────────────────────────────

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

            function formatOps(ops) {
              if (ops >= 1_000_000_000) return (ops / 1_000_000_000).toFixed(2) + " Gops/s";
              if (ops >= 1_000_000)     return (ops / 1_000_000).toFixed(2)     + " Mops/s";
              if (ops >= 1_000)         return (ops / 1_000).toFixed(2)         + " Kops/s";
              return ops.toFixed(0) + " ops/s";
            }

            function formatDelta(pct) {
              const sign = pct > 0 ? "+" : "";
              return sign + pct.toFixed(1) + "%";
            }

            function allocBadge(bytes) {
              let cls;
              if (bytes === 0)          cls = "alloc-zero";
              else if (bytes < 1024)    cls = "alloc-low";
              else if (bytes < 65536)   cls = "alloc-mid";
              else                      cls = "alloc-high";
              return '<span class="alloc-badge ' + cls + '">' + formatBytes(bytes) + '</span>';
            }

            function rankBadge(rank) {
              const cls = rank === 1 ? "rank-1" : rank === 2 ? "rank-2" : rank === 3 ? "rank-3" : "rank-n";
              return '<span class="rank ' + cls + '">' + rank + '</span>';
            }

            function benchLabel(b) {
              return b.Parameters ? b.Method + "(" + b.Parameters + ")" : b.Method;
            }

            function shortLabel(b) {
              if (b.Parameters) {
                return b.Parameters.includes("=") ? b.Parameters.split("=").slice(1).join("=") : b.Parameters;
              }
              return b.Method;
            }

            function barColor(index, total) {
              if (total <= 1) return "var(--accent-green)";
              const ratio = index / (total - 1);
              if (ratio < 0.33) return "var(--accent-green)";
              if (ratio < 0.66) return "var(--accent-blue)";
              if (ratio < 0.85) return "var(--accent-amber)";
              return "var(--accent-red)";
            }

            function barColorRaw(index, total) {
              if (total <= 1) return "hsla(142, 65%, 50%, 1)";
              const hue = 142 - (index / (total - 1)) * 142;
              return "hsla(" + hue + ", 65%, 50%, 1)";
            }

            function barColorAlpha(index, total, alpha) {
              if (total <= 1) return "hsla(142, 65%, 50%, " + alpha + ")";
              const hue = 142 - (index / (total - 1)) * 142;
              return "hsla(" + hue + ", 65%, 50%, " + alpha + ")";
            }

            function formatGen(val) {
              return val === 0 ? '<span style="color:var(--text-muted)">\u2014</span>' : String(val);
            }

            function parseParams(paramStr) {
              if (!paramStr) return {};
              const obj = {};
              paramStr.split("&").forEach(function(kv) {
                const parts = kv.split("=");
                const k = parts[0];
                const rest = parts.slice(1);
                if (k) obj[k.trim()] = rest.join("=").trim();
              });
              return obj;
            }

            function getParamKeys(sectionBenchmarks) {
              const keys = new Set();
              sectionBenchmarks.forEach(function(b) {
                Object.keys(parseParams(b.Parameters)).forEach(function(k) { keys.add(k); });
              });
              return [...keys];
            }

            function getMeasurementKey(b) {
              return b.Parameters ? b.Type + "." + b.Method + "|" + b.Parameters : b.Type + "." + b.Method;
            }

            // ── Metric Definitions ───────────────────────────────────────────────

            const metrics = [
              { key: "MeanNs",         label: "Mean",      format: formatNs },
              { key: "OpsPerSec",      label: "Ops/s",     format: formatOps },
              { key: "P95Ns",          label: "P95",       format: formatNs },
              { key: "AllocatedBytes", label: "Alloc",     format: formatBytes },
            ];

            const allMetrics = [
              { key: "MeanNs",         label: "Mean",      format: formatNs },
              { key: "OpsPerSec",      label: "Ops/s",     format: formatOps },
              { key: "ErrorNs",        label: "Error",     format: formatNs },
              { key: "StdDevNs",       label: "StdDev",    format: formatNs },
              { key: "P95Ns",          label: "P95",       format: formatNs },
              { key: "AllocatedBytes", label: "Allocated", format: formatBytes },
              { key: "Gen0",           label: "Gen0",      format: function(v) { return String(v); } },
              { key: "Gen1",           label: "Gen1",      format: function(v) { return String(v); } },
              { key: "Gen2",           label: "Gen2",      format: function(v) { return String(v); } },
            ];

            // ── Sparkline Histogram Builder ──────────────────────────────────────

            function buildSparkline(values) {
              if (!values || values.length < 2) return "";
              const BINS = 9;
              const MAX_H = 16;
              const min = Math.min.apply(null, values);
              const max = Math.max.apply(null, values);
              if (min === max) {
                // All values identical: single centered bar
                return '<span class="sparkline">' + Array(BINS).fill(0).map(function(_, i) {
                  return '<span class="bar" style="height:' + (i === 4 ? MAX_H : 1) + 'px"></span>';
                }).join("") + '</span>';
              }
              const binSize = (max - min) / BINS;
              const bins = new Array(BINS).fill(0);
              values.forEach(function(v) {
                let idx = Math.floor((v - min) / binSize);
                if (idx >= BINS) idx = BINS - 1;
                bins[idx]++;
              });
              const maxBin = Math.max.apply(null, bins);
              return '<span class="sparkline">' + bins.map(function(count) {
                const h = count === 0 ? 0 : Math.max(1, Math.round((count / maxBin) * MAX_H));
                return '<span class="bar" style="height:' + h + 'px"></span>';
              }).join("") + '</span>';
            }

            // ── Distribution Plot Builder ────────────────────────────────────────

            function buildDistPlot(sectionBenchmarks) {
              if (!measurements || sectionBenchmarks.length === 0) return "";

              // Gather all p50 values to determine the global scale
              var allMin = Infinity, allMax = -Infinity;
              var plotData = [];
              sectionBenchmarks.forEach(function(b, idx) {
                var m = measurements[getMeasurementKey(b)];
                if (!m || !m.Percentiles) return;
                var p = m.Percentiles;
                // BDN percentiles: P0, P25, P50, P67, P80, P85, P90, P95, P100
                var p0  = p.P0  != null ? p.P0  : b.MeanNs;
                var p25 = p.P25 != null ? p.P25 : b.MeanNs;
                var p50 = p.P50 != null ? p.P50 : b.MedianNs;
                var p75 = p.P75 != null ? p.P75 : b.MeanNs;
                var p100= p.P100!= null ? p.P100: b.MeanNs;
                allMin = Math.min(allMin, p0);
                allMax = Math.max(allMax, p100);
                plotData.push({ b: b, idx: idx, p0: p0, p25: p25, p50: p50, p75: p75, p100: p100 });
              });

              if (plotData.length === 0 || allMin === allMax) return "";

              var range = allMax - allMin;
              var pct = function(v) { return ((v - allMin) / range * 100).toFixed(2); };

              var rows = plotData.map(function(d) {
                var n = sectionBenchmarks.length;
                var color = barColorRaw(d.idx, n);
                return '<div class="dist-row">' +
                  '<div class="dist-label" title="' + benchLabel(d.b) + '">' + shortLabel(d.b) + '</div>' +
                  '<div class="dist-box">' +
                    '<div class="dist-whisker" style="left:' + pct(d.p0) + '%;width:' + (pct(d.p100) - pct(d.p0)).toFixed(2) + '%;background:' + color + ';opacity:0.3"></div>' +
                    '<div class="dist-box-rect" style="left:' + pct(d.p25) + '%;width:' + (pct(d.p75) - pct(d.p25)).toFixed(2) + '%;background:' + color + '"></div>' +
                    '<div class="dist-median" style="left:' + pct(d.p50) + '%;background:' + color + '"></div>' +
                  '</div>' +
                  '<div class="dist-value">' + formatNs(d.p50) + '</div>' +
                '</div>';
              });

              return '<div class="chart-card"><div class="chart-header"><div class="chart-title">' +
                '<i data-lucide="box-select" style="width:14px;height:14px"></i> Distribution</div></div>' +
                '<div class="dist-plot">' + rows.join("") + '</div></div>';
            }

            // ── Environment Panel Renderer (Task 6) ─────────────────────────────

            function renderEnvPanel() {
              if (!env) return;
              var el = document.getElementById("env-section");
              if (!el) return;

              var machineItems = [];
              if (env.OsVersion) machineItems.push({ label: "OS", value: env.OsVersion });
              if (env.ProcessorName) machineItems.push({ label: "CPU", value: env.ProcessorName });
              if (env.LogicalCoreCount) machineItems.push({
                label: "Cores",
                value: (env.PhysicalCoreCount || "?") + " physical, " + env.LogicalCoreCount + " logical"
              });
              if (env.Architecture) machineItems.push({ label: "Arch", value: env.Architecture });

              var configItems = [];
              if (env.RuntimeVersion) configItems.push({ label: "Runtime", value: env.RuntimeVersion });
              if (env.BenchmarkDotNetVersion) configItems.push({ label: "BDN Version", value: env.BenchmarkDotNetVersion });
              if (jobConfig) {
                if (jobConfig.Toolchain) configItems.push({ label: "Toolchain", value: jobConfig.Toolchain });
                if (jobConfig.WarmupCount != null) configItems.push({
                  label: "Warmup", value: jobConfig.WarmupCount + (jobConfig.WarmupCount === 1 ? " iteration" : " iterations")
                });
                if (jobConfig.IterationCount != null) configItems.push({
                  label: "Iterations", value: jobConfig.IterationCount + (jobConfig.IterationCount === 1 ? " iteration" : " iterations")
                });
              }
              if (env.Configuration) configItems.push({ label: "Configuration", value: env.Configuration });

              function gridHtml(items) {
                return '<div class="env-grid">' +
                  items.map(function(i) {
                    return '<span class="label">' + i.label + '</span><span class="value">' + i.value + '</span>';
                  }).join("") + '</div>';
              }

              el.innerHTML =
                '<div class="env-card">' +
                  '<div class="env-card-title"><i data-lucide="server" style="width:14px;height:14px"></i> Machine</div>' +
                  gridHtml(machineItems) +
                '</div>' +
                '<div class="env-card">' +
                  '<div class="env-card-title"><i data-lucide="flask-conical" style="width:14px;height:14px"></i> Configuration</div>' +
                  gridHtml(configItems) +
                '</div>';
            }

            // ── Bento Stat Cards Renderer (Task 7) ──────────────────────────────

            function renderBentoStats() {
              if (!benchmarks || benchmarks.length === 0) return;
              var el = document.getElementById("bento-stats");
              if (!el) return;

              var fastest = benchmarks.reduce(function(a, b) { return a.MeanNs <= b.MeanNs ? a : b; });
              var slowest = benchmarks.reduce(function(a, b) { return a.MeanNs >= b.MeanNs ? a : b; });
              var peakAlloc = benchmarks.reduce(function(a, b) { return a.AllocatedBytes >= b.AllocatedBytes ? a : b; });
              var topOps = benchmarks.reduce(function(a, b) { return a.OpsPerSec >= b.OpsPerSec ? a : b; });

              function card(icon, label, value, color, detail) {
                return '<div class="bento-card">' +
                  '<div class="bento-label"><i data-lucide="' + icon + '" style="width:14px;height:14px"></i> ' + label + '</div>' +
                  '<div class="bento-value ' + color + '">' + value + '</div>' +
                  '<div class="bento-detail">' + detail + '</div>' +
                '</div>';
              }

              el.innerHTML =
                card("zap", "Fastest", formatNs(fastest.MeanNs), "green", benchLabel(fastest)) +
                card("snail", "Slowest", formatNs(slowest.MeanNs), "red", benchLabel(slowest)) +
                card("memory-stick", "Peak Alloc", formatBytes(peakAlloc.AllocatedBytes), "amber", benchLabel(peakAlloc)) +
                card("activity", "Throughput", formatOps(topOps.OpsPerSec), "blue", benchLabel(topOps));
            }

            // ── Regression Summary Renderer (Task 9) ────────────────────────────

            function renderRegressionSummary() {
              if (!isComparison || !comparisons) return;
              var regressions = comparisons.filter(function(c) { return c.Classification === "Regressed"; });
              if (regressions.length === 0) return;

              var el = document.getElementById("regression-section");
              if (!el) return;

              regressions.sort(function(a, b) { return b.DeltaPercent - a.DeltaPercent; });

              var rows = regressions.map(function(c) {
                return '<tr>' +
                  '<td class="method-name">' + benchLabel(c.Current) + '</td>' +
                  '<td style="text-align:right">' + formatNs(c.Baseline ? c.Baseline.MeanNs : 0) + '</td>' +
                  '<td style="text-align:right">' + formatNs(c.Current.MeanNs) + '</td>' +
                  '<td style="text-align:right"><span class="alloc-badge alloc-high">' + formatDelta(c.DeltaPercent) + '</span></td>' +
                '</tr>';
              }).join("");

              el.style.display = "";
              el.innerHTML =
                '<div class="table-wrap" style="border-color:var(--accent-red);margin-bottom:20px">' +
                  '<div style="padding:14px 18px;font-weight:700;color:var(--accent-red);display:flex;align-items:center;gap:8px">' +
                    '<i data-lucide="alert-triangle" style="width:16px;height:16px"></i> ' +
                    'Regressions (>' + threshold + '% threshold) &mdash; ' + regressions.length + ' found' +
                  '</div>' +
                  '<table><thead><tr>' +
                    '<th style="text-align:left">Benchmark</th>' +
                    '<th style="text-align:right">Baseline</th>' +
                    '<th style="text-align:right">Current</th>' +
                    '<th style="text-align:right">Delta</th>' +
                  '</tr></thead><tbody>' + rows + '</tbody></table>' +
                '</div>';
            }

            // ── Per-Class Section Generator (Task 8) ────────────────────────────

            var pageState = {};
            var chartInstances = {};
            var chartLogState = {};
            var chartMetricState = {};

            function renderClassSection(className) {
              var classBenchmarks = benchmarks
                .filter(function(b) { return b.Type === className; })
                .sort(function(a, b) { return a.MeanNs - b.MeanNs; });

              if (classBenchmarks.length === 0) return;

              var shortName = className.split(".").pop();
              var sectionId = "section-" + className;
              var container = document.getElementById("class-sections");

              // Detect zero columns
              var anyAlloc = classBenchmarks.some(function(b) { return b.AllocatedBytes !== 0; });
              var anyGen0 = classBenchmarks.some(function(b) { return b.Gen0 !== 0; });
              var anyGen1 = classBenchmarks.some(function(b) { return b.Gen1 !== 0; });
              var anyGen2 = classBenchmarks.some(function(b) { return b.Gen2 !== 0; });

              var bestMean = classBenchmarks[0].MeanNs;
              var worstMean = classBenchmarks[classBenchmarks.length - 1].MeanNs;

              // Build comparison lookup
              var compMap = {};
              if (isComparison && comparisons) {
                comparisons.forEach(function(c) { compMap[c.Current.CompositeKey] = c; });
              }

              // Measurement count helper
              function measCount(b) {
                var m = measurements ? measurements[getMeasurementKey(b)] : null;
                return m && m.Values ? m.Values.length : 0;
              }

              // Sparkline helper
              function sparkFor(b) {
                var m = measurements ? measurements[getMeasurementKey(b)] : null;
                return m && m.Values ? buildSparkline(m.Values) : "";
              }

              // Table header
              var thHtml = '<tr>' +
                '<th data-sort="Method" style="text-align:left"><span class="th-inner"><i data-lucide="flask-conical" class="col-icon"></i>Method<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="count" class="text-right"><span class="th-inner"><i data-lucide="hash" class="col-icon"></i>Count<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="MeanNs" class="text-right"><span class="th-inner"><i data-lucide="bar-chart-2" class="col-icon"></i>Mean<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="OpsPerSec" class="text-right"><span class="th-inner"><i data-lucide="zap" class="col-icon"></i>Ops/s<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th class="text-center"><span class="th-inner"><i data-lucide="bar-chart" class="col-icon"></i>Dist.</span></th>' +
                '<th data-sort="ErrorNs" class="text-right"><span class="th-inner"><i data-lucide="shield-alert" class="col-icon"></i>Error<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="StdDevNs" class="text-right"><span class="th-inner"><i data-lucide="activity" class="col-icon"></i>StdDev<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="P95Ns" class="text-right"><span class="th-inner"><i data-lucide="percent" class="col-icon"></i>P95<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                '<th data-sort="AllocatedBytes" class="text-right"><span class="th-inner"><i data-lucide="memory-stick" class="col-icon"></i>Alloc<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                (anyGen0 ? '<th data-sort="Gen0" class="text-right"><span class="th-inner">Gen0<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' : '') +
                (anyGen1 ? '<th data-sort="Gen1" class="text-right"><span class="th-inner">Gen1<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' : '') +
                (anyGen2 ? '<th data-sort="Gen2" class="text-right"><span class="th-inner">Gen2<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' : '') +
                '<th data-sort="Rank" class="text-center"><span class="th-inner"><i data-lucide="trophy" class="col-icon"></i>Rank<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' +
                (isComparison ? '<th data-sort="DeltaPercent" class="text-right"><span class="th-inner">Delta<i data-lucide="arrow-up" class="sort-icon"></i></span></th>' : '') +
                (isComparison ? '<th class="text-center"><span class="th-inner">Class.</span></th>' : '') +
              '</tr>';

              // Table rows
              var rowsHtml = classBenchmarks.map(function(b) {
                var meanClass = b.MeanNs === bestMean ? " value-best" : (b.MeanNs === worstMean && classBenchmarks.length > 1 ? " value-worst" : "");
                var paramSub = b.Parameters ? '<div style="font-size:11px;color:var(--text-muted);margin-top:1px">' + b.Parameters + '</div>' : '';

                // Comparison cells
                var deltaCell = "";
                var classCell = "";
                if (isComparison) {
                  var comp = compMap[b.CompositeKey];
                  if (comp) {
                    var dcls = comp.Classification === "Regressed" ? "alloc-high" :
                               comp.Classification === "Improved" ? "alloc-low" :
                               comp.Classification === "New" ? "alloc-mid" : "alloc-zero";
                    var dlabel = comp.Classification === "New" ? "new" : formatDelta(comp.DeltaPercent);
                    deltaCell = '<td style="text-align:right"><span class="alloc-badge ' + dcls + '">' + dlabel + '</span></td>';
                    classCell = '<td style="text-align:center;font-size:11px;color:var(--text-muted)">' + comp.Classification + '</td>';
                  } else {
                    deltaCell = '<td style="text-align:right;color:var(--text-muted)">\u2014</td>';
                    classCell = '<td></td>';
                  }
                }

                return '<tr data-key="' + b.CompositeKey + '" data-method="' + b.Method + (b.Parameters ? " " + b.Parameters : "") + '">' +
                  '<td><span class="method-name">' + b.Method + '</span>' + paramSub + '</td>' +
                  '<td style="text-align:right;color:var(--text-muted)">' + measCount(b) + '</td>' +
                  '<td style="text-align:right" class="' + meanClass.trim() + '">' + formatNs(b.MeanNs) + '</td>' +
                  '<td style="text-align:right">' + formatOps(b.OpsPerSec) + '</td>' +
                  '<td style="text-align:center">' + sparkFor(b) + '</td>' +
                  '<td style="text-align:right">' + formatNs(b.ErrorNs) + '</td>' +
                  '<td style="text-align:right">' + formatNs(b.StdDevNs) + '</td>' +
                  '<td style="text-align:right">' + formatNs(b.P95Ns) + '</td>' +
                  '<td style="text-align:right">' + allocBadge(b.AllocatedBytes) + '</td>' +
                  (anyGen0 ? '<td style="text-align:right">' + formatGen(b.Gen0) + '</td>' : '') +
                  (anyGen1 ? '<td style="text-align:right">' + formatGen(b.Gen1) + '</td>' : '') +
                  (anyGen2 ? '<td style="text-align:right">' + formatGen(b.Gen2) + '</td>' : '') +
                  '<td style="text-align:center">' + rankBadge(b.Rank) + '</td>' +
                  deltaCell + classCell +
                '</tr>';
              }).join("");

              // Distribution plot
              var distHtml = buildDistPlot(classBenchmarks);

              // Build section HTML
              var html =
                '<div class="class-section" id="' + sectionId + '" style="margin-bottom:32px">' +
                  '<div class="section-header" onclick="toggleSection(\'' + sectionId + '\')">' +
                    '<span class="status-dot"></span>' +
                    '<i data-lucide="chevron-down" style="width:16px;height:16px;color:var(--text-muted);transition:transform 0.25s" id="chevron-' + sectionId + '"></i>' +
                    '<h2>' + shortName + '</h2>' +
                    '<span class="section-count">' + classBenchmarks.length + ' benchmarks</span>' +
                  '</div>' +
                  '<div class="section-body" id="body-' + sectionId + '">' +
                    '<div class="table-wrap">' +
                      '<table id="table-' + sectionId + '"><thead>' + thHtml + '</thead><tbody>' + rowsHtml + '</tbody></table>' +
                    '</div>' +
                    '<div class="table-pagination" id="pagination-' + sectionId + '"></div>' +
                    '<div class="charts-disclosure collapsed" id="charts-' + sectionId + '">' +
                      '<div class="charts-toggle" onclick="toggleCharts(\'' + sectionId + '\')">' +
                        '<i data-lucide="chevron-down" class="chevron-icon" style="width:14px;height:14px;color:var(--text-muted)"></i>' +
                        '<span style="font-size:13px;font-weight:600">Charts</span>' +
                      '</div>' +
                      '<div class="charts-body">' +
                        '<div class="charts-grid">' +
                          '<div class="chart-card">' +
                            '<div class="chart-header">' +
                              '<div class="chart-title"><i data-lucide="bar-chart-horizontal" style="width:14px;height:14px"></i> Comparison</div>' +
                              '<div style="display:flex;align-items:center;gap:8px">' +
                                '<div class="metric-pills" id="pills-' + sectionId + '">' +
                                  metrics.map(function(m, i) {
                                    return '<span class="metric-pill' + (i === 0 ? ' active' : '') + '" data-metric="' + m.key + '" data-section="' + sectionId + '">' + m.label + '</span>';
                                  }).join("") +
                                '</div>' +
                                '<span class="metric-pill" data-log-toggle="' + sectionId + '" title="Toggle log scale" style="font-size:11px">linear</span>' +
                              '</div>' +
                            '</div>' +
                            '<div class="chart-container" id="chartwrap-' + sectionId + '">' +
                              '<canvas id="chart-' + sectionId + '"></canvas>' +
                            '</div>' +
                          '</div>' +
                          distHtml +
                        '</div>' +
                      '</div>' +
                    '</div>' +
                  '</div>' +
                '</div>';

              container.insertAdjacentHTML("beforeend", html);

              // Initialize pagination
              pageState[sectionId] = { page: 0, size: 10 };
              paginate(sectionId);

              // Build initial chart
              buildBarChart(sectionId, "MeanNs");
            }

            // ── Table Sorting (Task 10) ──────────────────────────────────────────

            document.addEventListener("click", function(e) {
              var th = e.target.closest("th[data-sort]");
              if (!th) return;

              var table = th.closest("table");
              var tbody = table.querySelector("tbody");
              var key = th.dataset.sort;
              var isAsc = th.classList.contains("sort-asc");

              // Reset all headers in this table
              table.querySelectorAll("thead th[data-sort]").forEach(function(h) {
                h.classList.remove("sort-asc", "sort-desc");
                var icon = h.querySelector(".sort-icon");
                if (icon) icon.setAttribute("data-lucide", "arrow-up");
              });

              var dir = isAsc ? -1 : 1;
              var newDir = isAsc ? "sort-desc" : "sort-asc";
              th.classList.add(newDir);
              var activeIcon = th.querySelector(".sort-icon");
              if (activeIcon) {
                activeIcon.setAttribute("data-lucide", newDir === "sort-asc" ? "arrow-up" : "arrow-down");
              }

              // Build benchmark lookup
              var benchByKey = {};
              benchmarks.forEach(function(b) { if (!benchByKey[b.CompositeKey]) benchByKey[b.CompositeKey] = b; });

              // Sort rows
              var rows = Array.from(tbody.querySelectorAll("tr[data-key]"));
              rows.sort(function(ra, rb) {
                var ba = benchByKey[ra.dataset.key];
                var bb = benchByKey[rb.dataset.key];
                var va = 0, vb = 0;

                if (key === "count") {
                  var ma = measurements ? measurements[getMeasurementKey(ba)] : null;
                  var mb = measurements ? measurements[getMeasurementKey(bb)] : null;
                  va = ma && ma.Values ? ma.Values.length : 0;
                  vb = mb && mb.Values ? mb.Values.length : 0;
                } else if (key === "DeltaPercent") {
                  var ca = compMap[ba.CompositeKey];
                  var cb = compMap[bb.CompositeKey];
                  va = ca ? ca.DeltaPercent : 0;
                  vb = cb ? cb.DeltaPercent : 0;
                } else if (ba && bb) {
                  va = ba[key] != null ? ba[key] : (key === "Method" ? ba.Method : 0);
                  vb = bb[key] != null ? bb[key] : (key === "Method" ? bb.Method : 0);
                }

                if (typeof va === "string") return dir * va.localeCompare(vb);
                return dir * (va - vb);
              });

              rows.forEach(function(row) { tbody.appendChild(row); });

              // Re-paginate the section
              var sectionEl = th.closest(".class-section");
              if (sectionEl) {
                var sid = sectionEl.id;
                if (pageState[sid]) {
                  pageState[sid].page = 0;
                  paginate(sid);
                }
              }

              lucide.createIcons();
            });

            // Comparison map for sorting delta columns
            var compMap = {};
            if (isComparison && comparisons) {
              comparisons.forEach(function(c) { compMap[c.Current.CompositeKey] = c; });
            }

            // ── Pagination (Task 11) ─────────────────────────────────────────────

            function paginate(sectionId) {
              var state = pageState[sectionId];
              if (!state) return;

              var table = document.getElementById("table-" + sectionId);
              if (!table) return;
              var allRows = Array.from(table.querySelectorAll("tbody tr[data-key]"));

              // Filter by search if active
              var visibleRows = allRows.filter(function(r) { return r.dataset.searchHidden !== "true"; });
              var total = visibleRows.length;
              var size = state.size;
              var totalPages = Math.max(1, Math.ceil(total / size));
              if (state.page >= totalPages) state.page = totalPages - 1;
              if (state.page < 0) state.page = 0;
              var start = state.page * size;
              var end = Math.min(start + size, total);

              // Hide/show rows
              allRows.forEach(function(r) { r.style.display = "none"; });
              visibleRows.forEach(function(r, i) {
                r.style.display = (i >= start && i < end) ? "" : "none";
              });

              // Build pagination controls
              var pag = document.getElementById("pagination-" + sectionId);
              if (!pag) return;

              if (total <= size) {
                pag.innerHTML = '<span class="page-info">' + total + ' benchmarks</span>';
                return;
              }

              var btns = '';
              // Prev
              btns += '<span class="page-btn' + (state.page === 0 ? ' disabled' : '') + '" data-page="prev">&laquo;</span>';
              // Page numbers
              for (var p = 0; p < totalPages; p++) {
                if (totalPages > 7 && p > 1 && p < totalPages - 2 && Math.abs(p - state.page) > 1) {
                  if (p === 2 || p === totalPages - 3) btns += '<span class="page-btn disabled">&hellip;</span>';
                  continue;
                }
                btns += '<span class="page-btn' + (p === state.page ? ' active' : '') + '" data-page="' + p + '">' + (p + 1) + '</span>';
              }
              // Next
              btns += '<span class="page-btn' + (state.page === totalPages - 1 ? ' disabled' : '') + '" data-page="next">&raquo;</span>';

              // Page size selector
              var sizeOpts = [10, 25, 50, 100].map(function(s) {
                return '<option value="' + s + '"' + (s === size ? ' selected' : '') + '>' + s + '/page</option>';
              }).join("");

              pag.innerHTML =
                '<span class="page-info">' + (start + 1) + '\u2013' + end + ' of ' + total + ' benchmarks</span>' +
                '<div class="page-controls">' + btns +
                  '<select class="page-size-select" data-section="' + sectionId + '">' + sizeOpts + '</select>' +
                '</div>';

              // Bind page button clicks
              pag.querySelectorAll(".page-btn:not(.disabled)").forEach(function(btn) {
                btn.addEventListener("click", function() {
                  var v = btn.dataset.page;
                  if (v === "prev") state.page = Math.max(0, state.page - 1);
                  else if (v === "next") state.page = Math.min(totalPages - 1, state.page + 1);
                  else state.page = parseInt(v, 10);
                  paginate(sectionId);
                });
              });

              // Bind page size change
              var sizeSelect = pag.querySelector(".page-size-select");
              if (sizeSelect) {
                sizeSelect.addEventListener("change", function() {
                  state.size = parseInt(sizeSelect.value, 10);
                  state.page = 0;
                  paginate(sectionId);
                });
              }
            }

            // ── Search, Jump-to, Keyboard Navigation (Task 12) ──────────────────

            function filterSearch(query) {
              var term = (query || "").toLowerCase().trim();

              document.querySelectorAll(".class-section").forEach(function(section) {
                var sectionVisible = false;
                var sectionId = section.id;

                section.querySelectorAll("tr[data-key]").forEach(function(row) {
                  var text = (row.dataset.method || "").toLowerCase();
                  var match = !term || text.indexOf(term) >= 0;
                  row.dataset.searchHidden = match ? "false" : "true";
                  if (match) sectionVisible = true;
                });

                section.style.display = (!term || sectionVisible) ? "" : "none";

                // Re-paginate
                if (pageState[sectionId]) {
                  pageState[sectionId].page = 0;
                  paginate(sectionId);
                }
              });
            }

            function jumpToSection(value) {
              if (!value) return;
              var el = document.getElementById(value);
              if (el) {
                // Expand if collapsed
                var body = document.getElementById("body-" + value);
                if (body && body.style.display === "none") {
                  toggleSection(value);
                }
                el.scrollIntoView({ behavior: "smooth", block: "start" });
              }
              // Reset select
              var sel = document.getElementById("jump-select");
              if (sel) sel.value = "";
            }

            // Keyboard navigation
            var activeRowIndex = -1;
            document.addEventListener("keydown", function(e) {
              // Don't intercept when typing in an input
              if (e.target.tagName === "INPUT" || e.target.tagName === "SELECT" || e.target.tagName === "TEXTAREA") {
                if (e.key === "Escape") {
                  e.target.blur();
                  e.preventDefault();
                }
                return;
              }

              var allRows = Array.from(document.querySelectorAll("tr[data-key]")).filter(function(r) {
                return r.style.display !== "none" && r.dataset.searchHidden !== "true";
              });

              if (e.key === "/") {
                e.preventDefault();
                var search = document.getElementById("global-search");
                if (search) search.focus();
                return;
              }

              if (e.key === "j" || e.key === "k") {
                e.preventDefault();
                // Remove old active
                document.querySelectorAll("tr.active").forEach(function(r) { r.classList.remove("active"); });

                if (e.key === "j") activeRowIndex = Math.min(activeRowIndex + 1, allRows.length - 1);
                else activeRowIndex = Math.max(activeRowIndex - 1, 0);

                if (allRows[activeRowIndex]) {
                  allRows[activeRowIndex].classList.add("active");
                  allRows[activeRowIndex].scrollIntoView({ behavior: "smooth", block: "nearest" });
                }
                return;
              }

              if (e.key === "Enter") {
                e.preventDefault();
                // Expand the section of the active row
                var row = allRows[activeRowIndex];
                if (row) {
                  var section = row.closest(".class-section");
                  if (section) {
                    var charts = document.getElementById("charts-" + section.id);
                    if (charts && charts.classList.contains("collapsed")) {
                      toggleCharts(section.id);
                    }
                  }
                }
                return;
              }

              if (e.key === "Escape") {
                // Collapse all chart disclosures
                document.querySelectorAll(".charts-disclosure:not(.collapsed)").forEach(function(d) {
                  var sid = d.id.replace("charts-", "");
                  toggleCharts(sid);
                });
                // Clear active row
                document.querySelectorAll("tr.active").forEach(function(r) { r.classList.remove("active"); });
                activeRowIndex = -1;
              }
            });

            // ── Chart.js Bar Charts (Task 13) ────────────────────────────────────

            function getChartTextColor() {
              return getComputedStyle(document.documentElement).getPropertyValue("--text-secondary").trim() || "#8494a7";
            }

            function getChartGridColor() {
              return "rgba(128, 128, 128, 0.12)";
            }

            function buildBarChart(sectionId, metricKey) {
              var cls = sectionId.replace("section-", "");
              var sectionBenchmarks = benchmarks.filter(function(b) { return b.Type === cls; });
              var metric = allMetrics.find(function(m) { return m.key === metricKey; });
              if (!metric) return;

              var isLog = chartLogState[sectionId] || false;
              chartMetricState[sectionId] = metricKey;

              // Sort by metric
              var sorted = sectionBenchmarks.slice();
              if (metricKey === "OpsPerSec") {
                sorted.sort(function(a, b) { return (b[metricKey] || 0) - (a[metricKey] || 0); });
              } else {
                sorted.sort(function(a, b) { return (a[metricKey] || 0) - (b[metricKey] || 0); });
              }

              // Cap at 20 for readability
              var capped = sorted.length > 20 ? sorted.slice(0, 20) : sorted;

              var labels = capped.map(function(b) { return shortLabel(b); });
              var values = capped.map(function(b) { return b[metricKey] || 0; });
              var n = capped.length;
              var bgColors = capped.map(function(_, i) { return barColorAlpha(i, n, 0.8); });
              var borderColors = capped.map(function(_, i) { return barColorRaw(i, n); });
              var textColor = getChartTextColor();

              // Resize container
              var wrapEl = document.getElementById("chartwrap-" + sectionId);
              if (wrapEl) {
                var barH = 28;
                wrapEl.style.height = Math.max(200, capped.length * barH) + "px";
              }

              if (chartInstances[sectionId]) {
                chartInstances[sectionId].destroy();
              }

              var canvas = document.getElementById("chart-" + sectionId);
              if (!canvas) return;

              chartInstances[sectionId] = new Chart(canvas, {
                type: "bar",
                data: {
                  labels: labels,
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
                  animation: { duration: 600, easing: "easeOutQuart" },
                  plugins: {
                    legend: { display: false },
                    tooltip: {
                      callbacks: {
                        title: function(ctx) { return benchLabel(capped[ctx[0].dataIndex]); },
                        label: function(ctx) { return metric.format(ctx.raw); }
                      },
                      titleFont: { family: "monospace", size: 11 },
                      bodyFont: { family: "monospace", size: 13, weight: "bold" }
                    }
                  },
                  scales: {
                    x: {
                      type: isLog ? "logarithmic" : "linear",
                      ticks: {
                        color: textColor,
                        font: { family: "monospace", size: 10 },
                        callback: function(v) { return metric.format(v); }
                      },
                      grid: { color: getChartGridColor() }
                    },
                    y: {
                      ticks: {
                        color: textColor,
                        font: { family: "monospace", size: 10 }
                      },
                      grid: { display: false }
                    }
                  }
                }
              });
            }

            // Metric pill switching (delegated)
            document.addEventListener("click", function(e) {
              var pill = e.target.closest(".metric-pill[data-metric]");
              if (!pill) return;
              var sectionId = pill.dataset.section;
              var parent = pill.closest(".metric-pills");
              if (parent) {
                parent.querySelectorAll(".metric-pill").forEach(function(p) { p.classList.remove("active"); });
              }
              pill.classList.add("active");
              buildBarChart(sectionId, pill.dataset.metric);
            });

            // Log scale toggle (delegated)
            document.addEventListener("click", function(e) {
              var toggle = e.target.closest("[data-log-toggle]");
              if (!toggle) return;
              var sectionId = toggle.dataset.logToggle;
              chartLogState[sectionId] = !chartLogState[sectionId];
              toggle.textContent = chartLogState[sectionId] ? "log" : "linear";
              toggle.classList.toggle("active", chartLogState[sectionId]);
              buildBarChart(sectionId, chartMetricState[sectionId] || "MeanNs");
            });

            // ── Legends Renderer (Task 14) ───────────────────────────────────────

            function renderLegends() {
              var el = document.getElementById("legends-section");
              if (!el) return;

              el.innerHTML =
                '<div class="env-card-title" style="margin-bottom:10px"><i data-lucide="info" style="width:14px;height:14px"></i> Legend</div>' +
                '<div class="env-grid" style="grid-template-columns:auto 1fr;gap:6px 16px;font-size:13px">' +
                  '<span class="label">Mean</span><span class="value">Arithmetic mean of all iterations</span>' +
                  '<span class="label">Error</span><span class="value">Half of 99.9% confidence interval</span>' +
                  '<span class="label">StdDev</span><span class="value">Standard deviation of all iterations</span>' +
                  '<span class="label">P95</span><span class="value">95th percentile: worst 5% of iterations were slower</span>' +
                  '<span class="label">Rank</span><span class="value">Relative position within the benchmark class (1 = fastest)</span>' +
                  '<span class="label">Alloc</span><span class="value">Allocated memory per single operation (managed only)</span>' +
                  '<span class="label">1 ns</span><span class="value">1 Nanosecond (0.000000001 sec)</span>' +
                  '<span class="label">1 \u00B5s</span><span class="value">1 Microsecond (0.000001 sec)</span>' +
                  '<span class="label">1 ms</span><span class="value">1 Millisecond (0.001 sec)</span>' +
                '</div>';
            }

            // ── Theme Toggle (Task 14) ───────────────────────────────────────────

            function toggleTheme() {
              var root = document.documentElement;
              var current = root.getAttribute("data-theme");
              var next = current === "light" ? "dark" : "light";
              root.setAttribute("data-theme", next);
              localStorage.setItem("bdr-theme", next);
              // Rebuild charts so colors update
              Object.keys(chartInstances).forEach(function(sid) {
                buildBarChart(sid, chartMetricState[sid] || "MeanNs");
              });
            }

            // Apply stored theme on load
            (function() {
              var stored = localStorage.getItem("bdr-theme");
              if (stored) document.documentElement.setAttribute("data-theme", stored);
            })();

            // ── Section Toggle Functions ──────────────────────────────────────────

            function toggleSection(sectionId) {
              var body = document.getElementById("body-" + sectionId);
              var chevron = document.getElementById("chevron-" + sectionId);
              if (!body) return;
              var collapsed = body.style.display === "none";
              body.style.display = collapsed ? "" : "none";
              if (chevron) chevron.style.transform = collapsed ? "" : "rotate(-90deg)";
            }

            var allExpanded = true;
            function toggleAllSections() {
              allExpanded = !allExpanded;
              document.querySelectorAll(".class-section").forEach(function(section) {
                var body = document.getElementById("body-" + section.id);
                var chevron = document.getElementById("chevron-" + section.id);
                if (body) body.style.display = allExpanded ? "" : "none";
                if (chevron) chevron.style.transform = allExpanded ? "" : "rotate(-90deg)";
              });
              // Update toggle icon
              var toggleBtn = document.getElementById("toggle-all");
              if (toggleBtn) {
                var icon = toggleBtn.querySelector("[data-lucide]");
                if (icon) {
                  icon.setAttribute("data-lucide", allExpanded ? "unfold-vertical" : "fold-vertical");
                  lucide.createIcons();
                }
              }
            }

            function toggleCharts(sectionId) {
              var disc = document.getElementById("charts-" + sectionId);
              if (!disc) return;
              disc.classList.toggle("collapsed");
              // Build chart on first open
              if (!disc.classList.contains("collapsed") && !chartInstances[sectionId]) {
                buildBarChart(sectionId, "MeanNs");
              }
            }

            // ── Initialization ───────────────────────────────────────────────────

            renderEnvPanel();
            renderBentoStats();
            if (isComparison) renderRegressionSummary();
            classes.forEach(function(cls) { renderClassSection(cls); });
            renderLegends();

            // Badge count
            document.getElementById("benchmark-count-badge").textContent = benchmarks.length + " benchmarks";

            // Populate jump-to select
            (function() {
              var jumpSelect = document.getElementById("jump-select");
              if (!jumpSelect) return;
              classes.forEach(function(cls) {
                var opt = document.createElement("option");
                opt.value = "section-" + cls;
                opt.textContent = cls.split(".").pop();
                jumpSelect.appendChild(opt);
              });
            })();

            // Hash navigation
            if (location.hash) {
              var target = document.querySelector(location.hash);
              if (target) {
                setTimeout(function() { target.scrollIntoView({ behavior: "smooth" }); }, 100);
              }
            }

            // Initialize Lucide icons
            lucide.createIcons();
            </script>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
