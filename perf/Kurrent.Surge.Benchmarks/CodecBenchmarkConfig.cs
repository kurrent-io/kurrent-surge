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
/// <see cref="BenchmarkConfig"/> for the in-memory codec benchmarks.
/// </summary>
/// <remarks>
/// Runs at BenchmarkDotNet's default warmup and iteration counts. The other configs lower both, which
/// suits a benchmark spending seconds per invocation on live-server RPCs and starves one measuring tens
/// of nanoseconds — a single warmup iteration does not reach steady-state tiered JIT.
/// <para>
/// The toolchain stays in-process. The out-of-process one resolves the benchmark project by scanning
/// the tree for its csproj, and git worktrees under <c>.claude/worktrees/</c> put more than one copy
/// there, which it rejects outright.
/// </para>
/// <para>
/// Note for the Apache comparison arms: this assembly declares a <c>uuid</c> on <c>fixed(16)</c>
/// schema, so its module initializer calls <c>UuidLogicalType.Enable()</c> and every Apache arm here
/// runs against the widened uuid logical type rather than the built-in. That is a property of the
/// assembly, not of the toolchain — a separate process per class would not change it. The two agree on
/// a <c>string</c> base, which is the only base the Apache arms exercise.
/// </para>
/// </remarks>
public class CodecBenchmarkConfig : ManualConfig {
    public CodecBenchmarkConfig() {
        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumn(StatisticColumn.P95);
        AddColumn(RankColumn.Arabic);

        AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));

        // Ratios are per category. Without this, every method in a class divides by the one baseline,
        // which puts an encode benchmark's mean over an unrelated decode benchmark's.
        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.FullCompressed);

        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(50));
    }
}
