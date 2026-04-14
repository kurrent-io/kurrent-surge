using System.Text.Json.Serialization;

namespace Kurrent.Client;

[PublicAPI]
public class JsonSerializerOptionsBuilder : FluentBuilder<JsonSerializerOptionsBuilder, KurrentClientOptions> {
	public JsonSerializerOptionsBuilder(KurrentClientOptions options) : base(options) { }
	public JsonSerializerOptionsBuilder() : base(new KurrentClientOptions()) { }

	/// <summary>
	/// Sets the user-provided <see cref="JsonSerializerContext"/> instances for AOT-compatible
	/// JSON serialization of domain types.
	/// </summary>
	public JsonSerializerOptionsBuilder WithContexts(params JsonSerializerContext[] contexts) =>
		With(opts => opts with {
			Schema = opts.Schema with {
				Json = opts.Schema.Json with { UserJsonContexts = contexts }
			}
		});

	/// <summary>
	/// Sets whether the protobuf formatter should be used for JSON serialization.
	/// </summary>
	public JsonSerializerOptionsBuilder WithProtobufFormatter(bool useProtobufFormatter) =>
		With(opts => opts with {
			Schema = opts.Schema with {
				Json = opts.Schema.Json with { UseProtobufFormatter = useProtobufFormatter }
			}
		});
}
