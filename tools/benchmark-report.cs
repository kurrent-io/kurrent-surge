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
            <html lang="en">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Kurrent.Client Benchmark Report</title>
            <style>
              :root {
                --bg: #0d1117;
                --surface: #161b22;
                --border: #30363d;
                --text: #e6edf3;
                --text-muted: #8b949e;
                --accent: #58a6ff;
                --green: #3fb950;
                --yellow: #d29922;
                --red: #f85149;
                --purple: #bc8cff;
                --cyan: #79c0ff;
              }

              [data-theme="light"] {
                --bg:         #ffffff;
                --surface:    #f6f8fa;
                --border:     #d0d7de;
                --text:       #1f2328;
                --text-muted: #636c76;
                --accent:     #0969da;
                --green:      #1a7f37;
                --yellow:     #9a6700;
                --red:        #d1242f;
                --purple:     #8250df;
                --cyan:       #0550ae;
              }

              * { margin: 0; padding: 0; box-sizing: border-box; }

              body {
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
                background: var(--bg);
                color: var(--text);
                line-height: 1.5;
                padding: 2rem;
                max-width: 1400px;
                margin: 0 auto;
              }

              header {
                margin-bottom: 2rem;
                padding-bottom: 1.5rem;
                border-bottom: 1px solid var(--border);
              }

              header h1 {
                font-size: 1.5rem;
                font-weight: 600;
                margin-bottom: 0.25rem;
              }

              header p {
                color: var(--text-muted);
                font-size: 0.875rem;
              }

              .mode-badge {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.75rem;
                font-weight: 600;
                margin-left: 0.5rem;
              }

              .mode-single { background: rgba(88, 166, 255, 0.15); color: var(--accent); }
              .mode-comparison { background: rgba(210, 153, 34, 0.15); color: var(--yellow); }

              .env-grid {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
                gap: 0.75rem;
                margin-bottom: 2rem;
              }

              .env-card {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 0.75rem 1rem;
              }

              .env-card .label {
                font-size: 0.7rem;
                color: var(--text-muted);
                text-transform: uppercase;
                letter-spacing: 0.05em;
                margin-bottom: 0.125rem;
              }

              .env-card .value {
                font-size: 0.85rem;
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
              }

              .stats-row {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
                gap: 1rem;
                margin-bottom: 2rem;
              }

              .stat-card {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 1rem 1.25rem;
              }

              .stat-card .label {
                font-size: 0.75rem;
                color: var(--text-muted);
                text-transform: uppercase;
                letter-spacing: 0.05em;
                margin-bottom: 0.25rem;
              }

              .stat-card .value {
                font-size: 1.5rem;
                font-weight: 600;
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
              }

              .stat-card .unit {
                font-size: 0.875rem;
                color: var(--text-muted);
                font-weight: 400;
              }

              .section-nav {
                display: flex;
                gap: 0.5rem;
                margin-bottom: 2rem;
                flex-wrap: wrap;
              }

              .section-nav a {
                background: transparent;
                border: 1px solid var(--border);
                color: var(--text-muted);
                padding: 0.25rem 0.75rem;
                border-radius: 20px;
                font-size: 0.8rem;
                text-decoration: none;
                transition: all 0.15s;
              }

              .section-nav a:hover {
                border-color: var(--accent);
                color: var(--accent);
              }

              .class-section {
                margin-bottom: 2.5rem;
              }

              .class-header {
                display: flex;
                align-items: center;
                gap: 0.75rem;
                margin-bottom: 1rem;
                cursor: pointer;
                user-select: none;
              }

              .class-header h2 {
                font-size: 1.125rem;
                font-weight: 600;
              }

              .class-header .toggle {
                color: var(--text-muted);
                font-size: 0.75rem;
                transition: transform 0.2s;
              }

              .class-header .toggle.collapsed { transform: rotate(-90deg); }

              .class-body { overflow: hidden; }
              .class-body.collapsed { display: none; }

              .chart-section {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 1.5rem;
                margin-bottom: 1rem;
              }

              .chart-section h3 {
                font-size: 0.875rem;
                font-weight: 600;
                margin-bottom: 1rem;
                color: var(--text-muted);
              }

              .bar-chart {
                display: flex;
                flex-direction: column;
                gap: 0.5rem;
              }

              .bar-row {
                display: grid;
                grid-template-columns: 200px 1fr 100px;
                align-items: center;
                gap: 1rem;
                height: 32px;
              }

              .bar-label {
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                font-size: 0.75rem;
                color: var(--text-muted);
                text-align: right;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
              }

              .bar-track {
                height: 24px;
                background: rgba(255, 255, 255, 0.03);
                border-radius: 4px;
                overflow: hidden;
                position: relative;
              }

              .bar-fill {
                height: 100%;
                border-radius: 4px;
                transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
                opacity: 0.8;
              }

              .bar-fill-baseline {
                position: absolute;
                top: 0;
                height: 100%;
                border-radius: 4px;
                opacity: 0.3;
                border-right: 2px dashed var(--text-muted);
              }

              .bar-value {
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                font-size: 0.8rem;
                text-align: right;
                white-space: nowrap;
              }

              .table-section {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                overflow: hidden;
                margin-bottom: 1rem;
              }

              table {
                width: 100%;
                border-collapse: collapse;
                font-size: 0.875rem;
              }

              thead th {
                text-align: left;
                padding: 0.625rem 1rem;
                color: var(--text-muted);
                font-weight: 500;
                font-size: 0.75rem;
                text-transform: uppercase;
                letter-spacing: 0.05em;
                border-bottom: 1px solid var(--border);
                background: rgba(0, 0, 0, 0.2);
              }

              thead th.num { text-align: right; }

              tbody td {
                padding: 0.625rem 1rem;
                border-bottom: 1px solid rgba(48, 54, 61, 0.5);
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                font-size: 0.8rem;
              }

              tbody td.num { text-align: right; }
              tbody tr:last-child td { border-bottom: none; }
              tbody tr:hover { background: rgba(88, 166, 255, 0.04); }

              .rank-badge {
                display: inline-block;
                width: 22px;
                height: 22px;
                line-height: 22px;
                text-align: center;
                border-radius: 50%;
                font-size: 0.7rem;
                font-weight: 600;
              }

              .rank-1 { background: rgba(63, 185, 80, 0.2); color: var(--green); }
              .rank-2 { background: rgba(88, 166, 255, 0.2); color: var(--accent); }
              .rank-3 { background: rgba(210, 153, 34, 0.2); color: var(--yellow); }

              .alloc-badge {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 600;
              }

              .alloc-zero { background: rgba(63, 185, 80, 0.15); color: var(--green); }
              .alloc-nonzero { background: rgba(210, 153, 34, 0.15); color: var(--yellow); }

              .delta-improved {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 600;
                background: rgba(63, 185, 80, 0.15);
                color: var(--green);
              }

              .delta-regressed {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 600;
                background: rgba(248, 81, 73, 0.15);
                color: var(--red);
              }

              .delta-neutral {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 600;
                background: rgba(139, 148, 158, 0.15);
                color: var(--text-muted);
              }

              .delta-new {
                display: inline-block;
                padding: 0.125rem 0.5rem;
                border-radius: 10px;
                font-size: 0.7rem;
                font-weight: 600;
                background: rgba(188, 140, 255, 0.15);
                color: var(--purple);
              }

              .regression-section {
                background: var(--surface);
                border: 1px solid var(--red);
                border-radius: 8px;
                overflow: hidden;
                margin-bottom: 2rem;
              }

              .regression-section h2 {
                font-size: 1rem;
                font-weight: 600;
                padding: 1.25rem 1.5rem 1rem;
                color: var(--red);
              }

              .filter-row {
                display: flex;
                gap: 0.5rem;
                margin-bottom: 1.25rem;
                flex-wrap: wrap;
              }

              .filter-btn {
                background: transparent;
                border: 1px solid var(--border);
                color: var(--text-muted);
                padding: 0.25rem 0.75rem;
                border-radius: 20px;
                font-size: 0.8rem;
                cursor: pointer;
                transition: all 0.15s;
                font-family: inherit;
              }

              .filter-btn:hover {
                border-color: var(--accent);
                color: var(--accent);
              }

              .filter-btn.active {
                background: var(--accent);
                border-color: var(--accent);
                color: #0d1117;
              }

              footer {
                text-align: center;
                color: var(--text-muted);
                font-size: 0.75rem;
                padding-top: 1rem;
                border-top: 1px solid var(--border);
              }

              footer code {
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                color: var(--text-muted);
              }

              .highlights-grid {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
                gap: 1rem;
                margin-bottom: 2rem;
              }

              .highlight-card {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                overflow: hidden;
              }

              .highlight-card-header {
                padding: 0.625rem 1rem;
                font-size: 0.75rem;
                font-weight: 600;
                text-transform: uppercase;
                letter-spacing: 0.05em;
                border-bottom: 1px solid var(--border);
              }

              .highlight-fastest .highlight-card-header { color: var(--green); }
              .highlight-slowest  .highlight-card-header { color: var(--red);   }
              .highlight-memory   .highlight-card-header { color: var(--yellow); }

              .highlight-row {
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 0.5rem 1rem;
                border-bottom: 1px solid rgba(48, 54, 61, 0.4);
                gap: 0.5rem;
              }

              .highlight-row:last-child { border-bottom: none; }

              .highlight-name {
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                font-size: 0.75rem;
                color: var(--text-muted);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                min-width: 0;
              }

              .highlight-value {
                font-family: 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
                font-size: 0.8rem;
                font-weight: 600;
                white-space: nowrap;
              }

              .highlight-fastest .highlight-value { color: var(--green);  }
              .highlight-slowest  .highlight-value { color: var(--red);    }
              .highlight-memory   .highlight-value { color: var(--yellow); }

              .search-row {
                margin-bottom: 1.5rem;
              }

              .search-input {
                width: 100%;
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 8px;
                color: var(--text);
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
                font-size: 0.875rem;
                padding: 0.625rem 1rem;
                outline: none;
                transition: border-color 0.15s;
              }

              .search-input::placeholder { color: var(--text-muted); }
              .search-input:focus { border-color: var(--accent); }

              thead th[data-sort] {
                cursor: pointer;
                user-select: none;
                white-space: nowrap;
              }

              thead th[data-sort]:hover { color: var(--text); }

              thead th[data-sort]::after {
                content: " ↕";
                opacity: 0.3;
                font-size: 0.7rem;
              }

              thead th[data-sort].sort-asc::after  { content: " ↑"; opacity: 1; }
              thead th[data-sort].sort-desc::after { content: " ↓"; opacity: 1; }

              .theme-toggle {
                background: transparent;
                border: 1px solid var(--border);
                border-radius: 8px;
                color: var(--text-muted);
                cursor: pointer;
                font-size: 1rem;
                line-height: 1;
                padding: 0.375rem 0.625rem;
                transition: border-color 0.15s, color 0.15s;
                font-family: inherit;
              }

              .theme-toggle:hover {
                border-color: var(--accent);
                color: var(--accent);
              }

              html { scroll-behavior: smooth; }
            </style>
            </head>
            <body>

            <header style="display: flex; justify-content: space-between; align-items: flex-start;">
              <div>
                <h1>Kurrent.Client Benchmark Report
                  <span class="mode-badge {{(data.IsComparison ? "mode-comparison" : "mode-single")}}">
                    {{(data.IsComparison ? "comparison" : "single-run")}}
                  </span>
                </h1>
                <p>Generated {{timestamp}}</p>
              </div>
              <button class="theme-toggle" id="theme-toggle" title="Toggle dark/light theme">&#9790;</button>
            </header>

            <div id="env-section"></div>
            <div class="stats-row" id="stats-row"></div>
            <div id="highlights-section"></div>
            <div id="search-row" class="search-row"></div>
            <div class="section-nav" id="section-nav"></div>
            <div id="regression-section"></div>
            <div id="class-sections"></div>

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
                case "Regressed": return "delta-regressed";
                case "Improved":  return "delta-improved";
                case "New":       return "delta-new";
                default:          return "delta-neutral";
              }
            }

            function rankBadge(rank) {
              const cls = rank <= 3 ? `rank-${rank}` : "";
              return `<span class="rank-badge ${cls}">${rank}</span>`;
            }

            function allocBadge(bytes) {
              const cls = bytes === 0 ? "alloc-zero" : "alloc-nonzero";
              return `<span class="alloc-badge ${cls}">${formatBytes(bytes)}</span>`;
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
              return val === 0 ? `<span style="color:var(--text-muted)">—</span>` : String(val);
            }

            // ── Color palette for bars ──────────────────────────────────────────

            const barColors = [
              "var(--accent)", "var(--green)", "var(--purple)",
              "var(--yellow)", "var(--cyan)", "var(--red)"
            ];

            function barColor(index) {
              return barColors[index % barColors.length];
            }

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
                <div class="env-grid">
                  ${items.map(i => `
                    <div class="env-card">
                      <div class="label">${i.label}</div>
                      <div class="value">${i.value}</div>
                    </div>
                  `).join("")}
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
                <div class="stat-card">
                  <div class="label">Benchmarks</div>
                  <div class="value">${benchmarks.length}</div>
                </div>
                <div class="stat-card">
                  <div class="label">Fastest</div>
                  <div class="value" style="color: var(--green)">${formatNs(fastest)}</div>
                </div>
                <div class="stat-card">
                  <div class="label">Slowest</div>
                  <div class="value" style="color: var(--yellow)">${formatNs(slowest)}</div>
                </div>
                <div class="stat-card">
                  <div class="label">Total Allocations</div>
                  <div class="value">${formatBytes(totalAlloc)}</div>
                </div>
              `;

              if (isComparison && comparisons) {
                const regressions = comparisons.filter(c => c.Classification === "Regressed").length;
                const improvements = comparisons.filter(c => c.Classification === "Improved").length;
                cards += `
                  <div class="stat-card">
                    <div class="label">Regressions</div>
                    <div class="value" style="color: ${regressions > 0 ? 'var(--red)' : 'var(--green)'}">${regressions}</div>
                  </div>
                  <div class="stat-card">
                    <div class="label">Improvements</div>
                    <div class="value" style="color: ${improvements > 0 ? 'var(--green)' : 'var(--text-muted)'}">${improvements}</div>
                  </div>
                `;
              }

              document.getElementById("stats-row").innerHTML = cards;
            }

            // ── Highlights section ──────────────────────────────────────────────

            {
              function top5(arr) { return arr.slice(0, 5); }

              const bySpeed    = [...benchmarks].sort((a, b) => a.MeanNs - b.MeanNs);
              const fastest5   = top5(bySpeed);
              const slowest5   = top5([...benchmarks].sort((a, b) => b.MeanNs - a.MeanNs));
              const memory5    = top5([...benchmarks].filter(b => b.AllocatedBytes > 0)
                                       .sort((a, b) => b.AllocatedBytes - a.AllocatedBytes));

              function highlightRows(items, valueGetter) {
                return items.map(b => `
                  <div class="highlight-row">
                    <div class="highlight-name" title="${benchLabel(b)}">${benchLabel(b)}</div>
                    <div class="highlight-value">${valueGetter(b)}</div>
                  </div>
                `).join("");
              }

              const fastestCard = `
                <div class="highlight-card highlight-fastest">
                  <div class="highlight-card-header">Top 5 Fastest</div>
                  ${highlightRows(fastest5, b => formatNs(b.MeanNs))}
                </div>`;

              const slowestCard = `
                <div class="highlight-card highlight-slowest">
                  <div class="highlight-card-header">Top 5 Slowest</div>
                  ${highlightRows(slowest5, b => formatNs(b.MeanNs))}
                </div>`;

              const memoryRows = memory5.length > 0
                ? highlightRows(memory5, b => formatBytes(b.AllocatedBytes))
                : `<div class="highlight-row"><div class="highlight-name" style="color:var(--green)">All benchmarks allocate zero</div></div>`;

              const memoryCard = `
                <div class="highlight-card highlight-memory">
                  <div class="highlight-card-header">Top 5 Most Memory</div>
                  ${memoryRows}
                </div>`;

              document.getElementById("highlights-section").innerHTML = `
                <div class="highlights-grid">
                  ${fastestCard}
                  ${slowestCard}
                  ${memoryCard}
                </div>
              `;
            }

            // ── Section navigation ──────────────────────────────────────────────

            document.getElementById("section-nav").innerHTML = classes.map(c => {
              const shortName = c.split(".").pop();
              return `<a href="#section-${c}">${shortName}</a>`;
            }).join("");

            // ── Global search ───────────────────────────────────────────────────

            document.getElementById("search-row").innerHTML = `
              <input class="search-input" id="global-search" type="search"
                     placeholder="Search benchmarks..." autocomplete="off">
            `;

            document.getElementById("global-search").addEventListener("input", e => {
              const term = e.target.value.toLowerCase().trim();

              document.querySelectorAll(".class-section").forEach(section => {
                let sectionVisible = false;

                section.querySelectorAll(".bar-row[data-params], tbody tr[data-params]").forEach(el => {
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
                      <td>${benchLabel(c.Current)}</td>
                      <td class="num">${formatNs(c.Baseline?.MeanNs ?? 0)}</td>
                      <td class="num">${formatNs(c.Current.MeanNs)}</td>
                      <td class="num"><span class="delta-regressed">${formatDelta(c.DeltaPercent)}</span></td>
                    </tr>
                  `).join("");

                document.getElementById("regression-section").innerHTML = `
                  <div class="regression-section">
                    <h2>Regressions (>${threshold}% threshold)</h2>
                    <table>
                      <thead>
                        <tr>
                          <th>Benchmark</th>
                          <th class="num">Baseline</th>
                          <th class="num">Current</th>
                          <th class="num">Delta</th>
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
                  <div class="bar-row" data-params="${b.Parameters || ""}">
                    <div class="bar-label" title="${benchLabel(b)}">${benchLabel(b)}</div>
                    <div class="bar-track">
                      ${baselineBar}
                      <div class="bar-fill" style="width: ${pct}%; background: ${color};"></div>
                    </div>
                    <div class="bar-value" style="color: ${color}">${formatNs(b.MeanNs)}</div>
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
                    deltaCell = `<td class="num"><span class="${cls2}">${label}</span></td>`;
                  } else {
                    deltaCell = `<td class="num">—</td>`;
                  }
                }

                return `
                  <tr data-params="${b.Parameters || ""}">
                    <td>${benchLabel(b)}</td>
                    <td class="num">${formatNs(b.MeanNs)}</td>
                    <td class="num">${formatOps(b.OpsPerSec)}</td>
                    <td class="num" style="color: var(--text-muted)">${formatNs(b.ErrorNs)}</td>
                    <td class="num" style="color: var(--text-muted)">${formatNs(b.StdDevNs)}</td>
                    <td class="num">${formatNs(b.P95Ns)}</td>
                    <td class="num">${rankBadge(b.Rank)}</td>
                    <td class="num">${allocBadge(b.AllocatedBytes)}</td>
                    <td class="num">${formatGen(b.Gen0)}</td>
                    <td class="num">${formatGen(b.Gen1)}</td>
                    <td class="num">${formatGen(b.Gen2)}</td>
                    ${deltaCell}
                  </tr>
                `;
              }).join("");

              const deltaHeader = isComparison ? `<th class="num" data-sort="DeltaPercent">Delta</th>` : "";

              // Parameter filters
              let filterHtml = "";
              if (hasParams) {
                const filterBtns = [`<button class="filter-btn active" data-filter="all" data-section="${sectionId}">all</button>`];
                params.forEach(p => {
                  filterBtns.push(`<button class="filter-btn" data-filter="${p}" data-section="${sectionId}">${p}</button>`);
                });
                filterHtml = `<div class="filter-row">${filterBtns.join("")}</div>`;
              }

              const sectionHtml = `
                <div class="class-section" id="${sectionId}">
                  <div class="class-header" onclick="toggleSection('${sectionId}')">
                    <span class="toggle" id="toggle-${sectionId}">&#9660;</span>
                    <h2>${shortName}</h2>
                    <span style="color: var(--text-muted); font-size: 0.8rem;">${classBenchmarks.length} benchmarks</span>
                  </div>
                  <div class="class-body" id="body-${sectionId}">
                    ${filterHtml}
                    <div class="chart-section">
                      <h3>Latency</h3>
                      <div class="bar-chart" id="chart-${sectionId}">${barRows}</div>
                    </div>
                    <div class="table-section">
                      <table>
                        <thead>
                          <tr>
                            <th data-sort="Method">Benchmark</th>
                            <th class="num" data-sort="MeanNs">Mean</th>
                            <th class="num" data-sort="OpsPerSec">Ops/sec</th>
                            <th class="num" data-sort="ErrorNs">Error</th>
                            <th class="num" data-sort="StdDevNs">StdDev</th>
                            <th class="num" data-sort="P95Ns">P95</th>
                            <th class="num" data-sort="Rank">Rank</th>
                            <th class="num" data-sort="AllocatedBytes">Allocated</th>
                            <th class="num" data-sort="Gen0">Gen0</th>
                            <th class="num" data-sort="Gen1">Gen1</th>
                            <th class="num" data-sort="Gen2">Gen2</th>
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

            // ── Collapsible sections ────────────────────────────────────────────

            function toggleSection(sectionId) {
              const body = document.getElementById("body-" + sectionId);
              const toggle = document.getElementById("toggle-" + sectionId);
              body.classList.toggle("collapsed");
              toggle.classList.toggle("collapsed");
            }

            // ── Parameter filters ───────────────────────────────────────────────

            document.querySelectorAll(".filter-btn[data-section]").forEach(btn => {
              btn.addEventListener("click", () => {
                const sectionId = btn.dataset.section;
                const filter = btn.dataset.filter;

                // Update active state
                btn.closest(".filter-row").querySelectorAll(".filter-btn").forEach(b => b.classList.remove("active"));
                btn.classList.add("active");

                // Filter bars and rows
                const section = document.getElementById(sectionId);
                section.querySelectorAll(".bar-row[data-params], tr[data-params]").forEach(el => {
                  if (filter === "all" || el.dataset.params === filter) {
                    el.style.display = "";
                  } else {
                    el.style.display = "none";
                  }
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
              const root    = document.documentElement;
              const btn     = document.getElementById("theme-toggle");
              const stored  = localStorage.getItem("bdr-theme");

              function applyTheme(theme) {
                root.setAttribute("data-theme", theme);
                btn.innerHTML = theme === "light" ? "&#9790;" : "&#9728;";
                localStorage.setItem("bdr-theme", theme);
              }

              applyTheme(stored ?? "dark");

              btn.addEventListener("click", () => {
                applyTheme(root.getAttribute("data-theme") === "light" ? "dark" : "light");
              });
            })();
            </script>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
