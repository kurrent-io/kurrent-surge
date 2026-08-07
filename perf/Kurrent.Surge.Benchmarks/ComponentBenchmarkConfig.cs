using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Benchmarks;

/// <summary>
/// <see cref="BenchmarkConfig"/> with the InProcess per-case timeout raised. The component
/// benchmarks batch thousands of live-server RPCs per invocation; the degenerate
/// no-batching cases exceed the toolchain's default 5-minute ceiling.
/// </summary>
public class ComponentBenchmarkConfig : ManualConfig {
    public ComponentBenchmarkConfig() {
        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumn(StatisticColumn.P95);
        AddColumn(RankColumn.Arabic);

        AddJob(Job.Default
            .WithToolchain(new InProcessEmitToolchain(TimeSpan.FromMinutes(20), logOutput: false))
            .WithWarmupCount(1)
            .WithIterationCount(5));

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.FullCompressed);

        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(50));
    }
}
