using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Kurrent.Client.Benchmarks;

public class BenchmarkConfig : ManualConfig {
    public BenchmarkConfig() {
        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumn(StatisticColumn.P95);
        AddColumn(RankColumn.Arabic);

        // Use InProcess toolchain to avoid rebuild issues with gRPC code generation
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(1)
            .WithIterationCount(5));

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        AddExporter(JsonExporter.Full);

        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(50));
    }
}
