using Google.Protobuf;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Transcoder.Probe;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The schema under test, loaded the way a caller gets one — as text, never as a compiled descriptor.
/// </summary>
public static class ProbeSchema {
    public const string OrderTypeName = "transcoder.probe.Order";
    public const string MoneyTypeName = "transcoder.probe.Money";

    public static string Text { get; } = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "protos", "transcoder_probe.proto"));

    public static ProtoSchemaSet Set { get; } = ProtoSchemaSet.Build(new ProtoFile("transcoder_probe.proto", Text));

    /// <summary>
    /// Google's own formatter, which is the oracle every case is compared against.
    /// </summary>
    /// <remarks>
    /// A registry is required: <c>Any</c> names its payload type at run time, and the formatter
    /// refuses to expand one it cannot resolve.
    /// </remarks>
    public static JsonFormatter Oracle { get; } = new(
        JsonFormatter.Settings.Default.WithTypeRegistry(TypeRegistry.FromFiles(TranscoderProbeReflection.Descriptor)));

    public static JsonFormatter OracleWithDefaults { get; } = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithTypeRegistry(TypeRegistry.FromFiles(TranscoderProbeReflection.Descriptor)));

    public static string Write(ReadOnlyMemory<byte> data, string messageTypeName = OrderTypeName, ProtobufJsonWriterOptions? options = null) =>
        new ProtobufJsonWriter(Set, messageTypeName, options).Write(data);

    public static MessageDescriptor Order => Set.FindMessage(OrderTypeName);
}
