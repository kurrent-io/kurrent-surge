using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

namespace Benchmarks;

/// <summary>
/// Schema text to JSON Schema and back.
/// </summary>
/// <remarks>
/// A registration-time cost rather than a per-record one, measured so a regression is visible rather
/// than because it is hot. There is no competing implementation to compare against — no .NET package
/// reads or prints <c>.proto</c> — so the axes are closure size and the previous measurement.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class ProtobufJsonSchemaBenchmarks {
    /// <summary>
    /// The message whose closure is converted.
    /// </summary>
    /// <remarks>
    /// <c>Order</c> reaches two nested messages, an enum, a map, a oneof and seven well-known types.
    /// <c>Money</c> is two scalars and reaches nothing, which is the floor the rest is measured above.
    /// </remarks>
    [Params(ProbeSchema.OrderTypeName, ProbeSchema.MoneyTypeName)]
    public string MessageTypeName { get; set; } = ProbeSchema.OrderTypeName;

    static readonly ProtobufJsonSchemaOptions CanonicalOptions = new() { Canonical = true };

    ProtoSchemaSet _schema   = null!;
    string         _exported = null!;

    [GlobalSetup]
    public void Setup() {
        _schema = ProbeSchema.Set;

        // Import reads a document, so it is handed one rather than charged for producing it.
        _exported = ProtobufJsonSchema.Export(_schema, MessageTypeName);
    }

    [Benchmark(Baseline = true, Description = "Export")]
    public int Export() => ProtobufJsonSchema.Export(_schema, MessageTypeName).Length;

    /// <remarks>
    /// Canonical sorts every object key, which it does by rebuilding the document from deep clones.
    /// The difference from the arm above is what that costs.
    /// </remarks>
    [Benchmark(Description = "Export (canonical)")]
    public int ExportCanonical() => ProtobufJsonSchema.Export(_schema, MessageTypeName, CanonicalOptions).Length;

    [Benchmark(Description = "Import (prints .proto)")]
    public int Import() => ProtobufJsonSchema.Import(_exported).Length;
}
