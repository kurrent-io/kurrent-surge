using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Schema;

namespace Benchmarks;

/// <summary>
/// Avro schema text to a JSON Schema document.
/// </summary>
/// <remarks>
/// A registration-time cost rather than a per-record one, measured so a regression is visible rather
/// than because it is hot.
/// <para>
/// The two canonical arms differ in one function. Allocation is the primary reading, because it is
/// deterministic; wall-clock is read against the non-canonical arm, which neither canonical
/// implementation touches and which therefore shows machine drift between runs.
/// </para>
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class AvroJsonSchemaBenchmarks {
    /// <summary>
    /// The schema whose closure is converted.
    /// </summary>
    /// <remarks>
    /// <c>BenchComplexOrder</c> reaches a nested record, an array, a map, an enum and a nullable union.
    /// <c>BenchFlatOrder</c> is three scalars and reaches nothing, which is the floor the other is
    /// measured above.
    /// </remarks>
    [Params("BenchComplexOrder", "BenchFlatOrder")]
    public string SchemaName { get; set; } = "BenchComplexOrder";

    static readonly JsonSchemaExportOptions CanonicalOptions = new() { Canonical = true };

    SchemaSource _source = null!;

    [GlobalSetup]
    public void Setup() =>
        _source = SchemaSource.Text(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", $"{SchemaName}.avsc")));

    /// <remarks>The drift control: no canonical pass runs here, so this arm should not move.</remarks>
    [Benchmark(Baseline = true, Description = "Export")]
    public int Export() => AvroJsonSchema.Instance.Export(_source).ToJsonString().Length;

    [Benchmark(Description = "Export (canonical)")]
    public int ExportCanonical() => AvroJsonSchema.Instance.Export(_source, CanonicalOptions).ToJsonString().Length;

    /// <remarks>
    /// The canonical pass as it was written before, kept as a measurement arm rather than as code that
    /// runs. It clones each child and then rebuilds it anyway, so a subtree at depth d is cloned d times.
    /// </remarks>
    [Benchmark(Description = "Export (canonical, clone per level)")]
    public int ExportCanonicalCloningEveryLevel() =>
        CloneEveryLevel(AvroJsonSchema.Instance.Export(_source))!.ToJsonString().Length;

    static JsonNode? CloneEveryLevel(JsonNode? node) => node switch {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, CloneEveryLevel(p.Value?.DeepClone())))),

        JsonArray a => new JsonArray(a.Select(i => CloneEveryLevel(i?.DeepClone())).ToArray()),

        _ => node?.DeepClone()
    };
}
