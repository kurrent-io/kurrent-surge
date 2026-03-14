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
