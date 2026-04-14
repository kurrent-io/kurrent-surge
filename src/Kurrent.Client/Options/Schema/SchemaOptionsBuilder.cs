using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;

namespace Kurrent.Client;

[PublicAPI]
public class SchemaOptionsBuilder : FluentBuilder<SchemaOptionsBuilder, KurrentClientOptions> {
    public SchemaOptionsBuilder(KurrentClientOptions options) : base(options) { }

    public SchemaOptionsBuilder() : base(new KurrentClientOptions()) { }

    /// <summary>
    /// Configures the JSON serializer options using a sub-builder.
    /// </summary>
    public SchemaOptionsBuilder ConfigureJson(Action<JsonSerializerOptionsBuilder> configure) =>
        WithBuilder(configure);

    /// <summary>
    /// Configures the Protobuf serializer options using a sub-builder.
    /// </summary>
    public SchemaOptionsBuilder ConfigureProtobuf(Action<ProtobufSerializerOptionsBuilder> configure) =>
        WithBuilder(configure);

    /// <summary>
    /// Sets whether schemas should be automatically registered.
    /// </summary>
    public SchemaOptionsBuilder WithAutoRegister(bool autoRegister) =>
        With(opts => opts with { Schema = opts.Schema with { AutoRegister = autoRegister } });

    /// <summary>
    /// Sets whether messages should be validated against their schemas.
    /// </summary>
    public SchemaOptionsBuilder WithValidation(bool validate) =>
        With(opts => opts with { Schema = opts.Schema with { PreValidate = validate } });

    /// <summary>
    /// Sets the strategy used for generating schema names.
    /// </summary>
    public SchemaOptionsBuilder WithNameStrategy(ISchemaNameStrategy nameStrategy) =>
        With(opts => opts with { Schema = opts.Schema with { NameStrategy = nameStrategy } });

    /// <summary>
    /// Configures message type mappings for schema resolution.
    /// </summary>
    /// <param name="configure">
    /// An action to configure the message mapping builder.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a <b>new message mapper</b> and applies the specified configuration.
    /// Any existing message type mappings will be replaced with the newly configured mappings.
    /// </para>
    /// </remarks>
    public SchemaOptionsBuilder ConfigureMessages(Action<MessageMappingBuilder> configure) =>
        With(opts => opts with { Schema = opts.Schema with { Mapper = new MessageMappingBuilder().With(configure).Build() } });

    /// <summary>
    /// Sets a pre-configured message type mapper for schema resolution.
    /// </summary>
    /// <param name="mapper">The message type mapper instance to use.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method performs <b>complete replacement</b> of the Mapper property.
    /// Use this when you have a pre-configured <see cref="MessageTypeMapper"/> instance.
    /// </para>
    /// </remarks>
    public SchemaOptionsBuilder ConfigureMessages(MessageTypeMapper mapper) =>
        With(opts => opts with { Schema = opts.Schema with { Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper)) } });
}
