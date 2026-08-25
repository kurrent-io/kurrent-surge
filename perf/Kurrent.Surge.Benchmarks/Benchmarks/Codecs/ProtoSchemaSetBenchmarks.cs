using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;
using ProtobufTranscoder;

namespace Benchmarks;

/// <summary>
/// Schema text to resolved descriptors.
/// </summary>
/// <remarks>
/// The cost <see cref="ProtobufJsonWriterBenchmarks"/> hoists into its setup, measured on its own.
/// A writer is built once per schema and reused, so this is a registration-time cost rather than a
/// per-record one — it is measured because the lexer and parser are shared, not because it is hot.
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class ProtoSchemaSetBenchmarks {
    string _text = null!;

    [GlobalSetup]
    public void Setup() => _text = ProbeSchema.Text;

    [Benchmark(Baseline = true, Description = "original (ProtoDefinitions)")]
    public int Original() => ProtoDefinitions.Build(new ProtoSchema("transcoder_probe.proto", _text)).GetHashCode();

    [Benchmark(Description = "ProtoSchemaSet (lexer + parser + link)")]
    public int SchemaSet() => ProtoSchemaSet.Build(new ProtoFile("transcoder_probe.proto", _text)).Files.Count;
}
