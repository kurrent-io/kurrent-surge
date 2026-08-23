using System.Buffers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;
using ProtobufTranscoder;
using Transcoder.Probe;
using Duration = Google.Protobuf.WellKnownTypes.Duration;
using SchemaWriter = Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed.ProtobufJsonWriter;

namespace Benchmarks;

/// <summary>
/// Protobuf payload to JSON, guided by schema text.
/// </summary>
/// <remarks>
/// Every arm answers the same question: bytes plus a schema in, canonical JSON out, with no compiled
/// type for the message. The baseline materialises a dynamic message first; the typed arm is the
/// ceiling rather than a competitor — it is what the work costs when the type is known at build time
/// and no schema is consulted at all.
/// <para>
/// Schema resolution is hoisted into setup for every arm, so the measurement is the transcode and
/// not the parse. That matches how each is used: a writer is built once per schema and reused.
/// </para>
/// </remarks>
[MemoryDiagnoser(displayGenColumns: false), Config(typeof(CodecBenchmarkConfig))]
[BenchmarkCategory("Codecs")]
public class ProtobufJsonWriterBenchmarks {
    /// <summary>Line count, which drives payload size and the depth of nested-message work.</summary>
    [Params(0, 8, 64)]
    public int Lines { get; set; }

    byte[]           _payload      = null!;
    SchemaWriter     _schemaWriter = null!;
    ProtoDefinitions _original     = null!;

    // Both schema-driven arms write UTF-8 into this and return its written count, so neither pays
    // for a UTF-16 string the other avoids. Reset per invocation; the capacity reached during warmup
    // is kept, so buffer growth is not measured either.
    ArrayBufferWriter<byte> _destination = null!;
    Utf8JsonWriter          _writer      = null!;

    // Google's formatter writes characters and offers no UTF-8 destination, so the ceiling arm
    // reuses a builder rather than being charged for a transcode the API cannot avoid.
    StringBuilder _characters = null!;
    StringWriter  _text       = null!;

    [GlobalSetup]
    public void Setup() {
        var schema = ProbeSchema.Set;

        _schemaWriter = new SchemaWriter(schema, ProbeSchema.OrderTypeName);

        // The original transcoder resolves its own descriptors from the same text, so both sides of
        // the comparison start from the schema a caller actually holds.
        _original = ProtoDefinitions.Build(new ProtoSchema("transcoder_probe.proto", ProbeSchema.Text));

        _payload     = BuildOrder(Lines).ToByteArray();
        _destination = new ArrayBufferWriter<byte>(16 * 1024);
        _writer      = new Utf8JsonWriter(_destination);
        _characters  = new StringBuilder(16 * 1024);
        _text        = new StringWriter(_characters);
    }

    [GlobalCleanup]
    public void Cleanup() {
        _writer.Dispose();
        _text.Dispose();
    }

    [Benchmark(Baseline = true, Description = "original (DynamicMessage)")]
    public int Original() {
        _destination.ResetWrittenCount();

        ProtoTranscode.ToJson(_payload, _original, ProbeSchema.OrderTypeName, _destination);

        return _destination.WrittenCount;
    }

    [Benchmark(Description = "ProtobufJsonWriter (schema-driven)")]
    public int SchemaDriven() {
        _destination.ResetWrittenCount();
        _writer.Reset(_destination);

        _schemaWriter.Write(_payload, _writer);

        _writer.Flush();

        return _destination.WrittenCount;
    }

    /// <remarks>
    /// The ceiling rather than a competitor: the message type is known at build time, so no schema
    /// is consulted. It still starts from bytes and ends at JSON, which is what makes it comparable
    /// — but it materialises the message first and emits UTF-16, so its allocation figure is not
    /// measuring the same output the schema-driven arms produce.
    /// </remarks>
    [Benchmark(Description = "typed ceiling (Google, compiled types)")]
    public int Typed() {
        _characters.Clear();

        ProbeSchema.Oracle.Format(Order.Parser.ParseFrom(_payload), _text);

        return _characters.Length;
    }

    static Order BuildOrder(int lines) {
        var order = new Order {
            OrderId   = "8d2b1f6c-1c4e-4a2f-9d3a-77b0c1a5e4d2",
            Priority  = Priority.High,
            Flags     = { 1, 2, 3, 5, 8 },
            Payload   = ByteString.CopyFrom(0xCA, 0xFE, 0xBA, 0xBE),
            Ratio     = 0.125,
            Score     = 2.5f,
            Sequence  = ulong.MaxValue,
            Delta     = -7,
            Checksum  = 0xDEADBEEFCAFEBABE,
            Archived  = true,
            Note      = "handle with care",
            Web       = "storefront",
            CreatedAt = new Timestamp { Seconds = 1_724_000_000, Nanos = 42 },
            Ttl       = new Duration { Seconds = 90 },
            Label     = "shipped",
            Retries   = 3,
            Tags      = { ["channel"] = "web", ["region"] = "emea" },
            Totals    = { [1] = new Money { Currency = "USD", Amount = 1_998 } }
        };

        for (var i = 0; i < lines; i++)
            order.Lines.Add(new Line {
                Sku      = $"SKU-{i:D6}",
                Quantity = i + 1,
                Price    = new Money { Currency = "USD", Amount = 100 + i }
            });

        return order;
    }
}
