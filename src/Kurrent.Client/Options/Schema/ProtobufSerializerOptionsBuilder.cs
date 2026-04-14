using Kurrent.Client.Schema.Serialization.Protobuf;

namespace Kurrent.Client;

[PublicAPI]
public class ProtobufSerializerOptionsBuilder : FluentBuilder<ProtobufSerializerOptionsBuilder, KurrentClientOptions> {
	public ProtobufSerializerOptionsBuilder(KurrentClientOptions options) : base(options) { }
	public ProtobufSerializerOptionsBuilder() : base(new KurrentClientOptions()) { }

	/// <summary>
	/// Sets the <see cref="ProtoSerializerContext"/> for AOT-compatible protobuf deserialization.
	/// </summary>
	public ProtobufSerializerOptionsBuilder WithContext(ProtoSerializerContext context) =>
		With(opts => opts with {
			Schema = opts.Schema with {
				Protobuf = opts.Schema.Protobuf with { Context = context }
			}
		});
}
