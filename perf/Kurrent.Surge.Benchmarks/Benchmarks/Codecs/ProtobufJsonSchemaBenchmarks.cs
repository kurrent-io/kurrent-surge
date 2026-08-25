using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;
using Kurrent.Surge.Schema;

namespace Benchmarks;

/// <summary>
/// Schema text to JSON Schema, and payload to JSON.
/// </summary>
/// <remarks>
/// A registration-time cost rather than a per-record one, measured so a regression is visible rather
/// than because it is hot. There is no competing implementation to compare against — no .NET package
/// reads <c>.proto</c> — so the axes are closure size and the previous measurement.
/// <para>
/// The transcode arm shares the source instance with the export arms, which is what puts it on the
/// cached path the transcoder documents. Building a fresh source per call would measure the parse.
/// </para>
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class ProtobufJsonSchemaBenchmarks {
    /// <summary>
    /// The message whose closure is converted.
    /// </summary>
    /// <remarks>
    /// <c>Order</c> reaches two nested messages, an enum, a map and seven well-known types.
    /// <c>Money</c> is two scalars and reaches nothing, which is the floor the rest is measured above.
    /// </remarks>
    [Params(ProbeSchema.OrderTypeName, ProbeSchema.MoneyTypeName)]
    public string MessageTypeName { get; set; } = ProbeSchema.OrderTypeName;

    static readonly JsonSchemaExportOptions CanonicalOptions = new() { Canonical = true };

    SchemaSource _source  = null!;
    byte[]       _payload = [];

    [GlobalSetup]
    public void Setup() {
        _source = SchemaSource.Text(ProbeSchema.Text, MessageTypeName);

        // Transcoding an empty message still walks the schema, which is the work being measured.
        _payload = [];

        // Resolves the schema once, so the measured calls take the cached path.
        ProtobufSchemaTranscoder.Instance.ToJson(_source, _payload);
    }

    [Benchmark(Baseline = true, Description = "Export")]
    public int Export() => ProtobufJsonSchema.Instance.ExportAsString(_source).Length;

    /// <remarks>
    /// Canonical sorts every object key, which it does by rebuilding the document. The difference
    /// from the arm above is what that costs.
    /// </remarks>
    [Benchmark(Description = "Export (canonical)")]
    public int ExportCanonical() => ProtobufJsonSchema.Instance.ExportAsString(_source, CanonicalOptions).Length;

    [Benchmark(Description = "Transcode")]
    public int Transcode() => ProtobufSchemaTranscoder.Instance.ToJson(_source, _payload).Length;
}
