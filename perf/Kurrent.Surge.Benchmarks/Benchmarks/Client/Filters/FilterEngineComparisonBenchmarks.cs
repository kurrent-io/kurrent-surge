using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.RecordFilters;
using Kurrent.Surge.Schema;

namespace Benchmarks;

/// <summary>
/// Runs the same expression over the same record through both engines: the interpreted tree in
/// Kurrent.Surge.Core, which resolves columns and sniffs types per record, and the compiled predicate
/// in Kurrent.Surge.RecordFilters, which settles both at parse time. Interpreted is the baseline, so
/// the Ratio column reads as the cost of the change.
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class FilterEngineComparisonBenchmarks {
    IFilterExpression _interpreted = null!;
    Filter<Record>    _compiled    = null!;
    Record            _record      = null!;

    [ParamsSource(nameof(Scenarios))]
    public ExpressionFilterEvalBenchmarks.EvalScenario Scenario { get; set; } = null!;

    public static IEnumerable<ExpressionFilterEvalBenchmarks.EvalScenario> Scenarios =>
        ExpressionFilterEvalBenchmarks.Scenarios;

    [GlobalSetup]
    public void Setup() {
        _interpreted = ExpressionFilterParser.Parse(Scenario.Expression);
        _compiled    = RecordFilterSchema.Default.Parse(Scenario.Expression);
        _record      = Scenario.CreateRecord();
    }

    [Benchmark(Baseline = true)]
    public bool Interpreted() => _interpreted.IsMatch(_record);

    [Benchmark]
    public bool Compiled() => _compiled.IsMatch(_record);
}

/// <summary>
/// Mirrors <see cref="RecordPropertyMap"/> column for column, so the two engines are given identical
/// column definitions and the measurement isolates how they evaluate rather than what they expose.
/// </summary>
static class RecordFilterSchema {
    public static readonly FilterSchema<Record> Default = FilterSchema<Record>.Create(
        schema => schema
            .Scalar(r => r.Id,                        "record_id")
            .Scalar(r => r.Position.LogPosition.Value, "log_position", "position", "pos")
            .Scalar(r => r.Stream.Category,            "category")
            .Scalar(r => r.Stream.Value,               "stream")
            .Scalar(r => r.Revision.Value,             "stream_revision", "revision")
            .Scalar(r => r.Schema.Schema.Value,        "schema_name", "schema")
            .Scalar(r => r.Schema.VersionId.Value,     "schema_id")
            .Scalar(r => r.Schema.DataFormat,          "schema_format")
            .Scalar(r => new DateTimeOffset(r.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                                                       "created_at", "timestamp", "ts")
            .Json(ResolveData,                         "data")
            .Map(ResolveMetadata,                      "metadata", "properties", "props")
            .Qualifier("rec.")
            .TrimPrefix("field_")
    );

    static object? ResolveData(Record record, string? key) {
        var node = record.DecodedValueAsJson;
        if (node is null) return null;

        if (key is null) return node.ToJsonString();

        foreach (var segment in key.Split('.'))
            node = node?[segment];

        return node switch {
            JsonValue value => value.ToString(),
            _               => node?.ToJsonString()
        };
    }

    static object? ResolveMetadata(Record record, string? key) {
        if (key is null) return record.Metadata;

        return record.Metadata.TryGetValue<object>(key, out var value) ? value : null;
    }
}
