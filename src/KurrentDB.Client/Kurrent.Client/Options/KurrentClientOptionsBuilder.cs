#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

using Grpc.Core.Interceptors;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client;

/// <summary>
/// Builder for creating <see cref="KurrentClientOptions"/> instances in a fluent manner.
/// </summary>
/// <remarks>
/// <para>
/// Provides a fluent API for configuring all aspects of KurrentDB client options,
/// including connection settings, security, resilience strategies, and schema configuration.
/// </para>
/// <para>
/// Start with <see cref="Create"/> for default options or <see cref="FromConnectionString"/> to
/// initialize from a connection string.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create with defaults and customize
/// var options = KurrentClientOptionsBuilder.Create()
///     .ConfigureConnection(builder => builder
///         .WithConnectionName("my-service")
///         .WithEndpoint("kurrentdb.example.com", 2113)
///         .WithSecurityOptions(KurrentClientSecurityOptions.MutualTls(
///             clientCertPath: "/path/to/client.crt",
///             clientKeyPath: "/path/to/client.key")))
///     .Build();
///
/// // Create from connection string
/// var options = KurrentClientOptionsBuilder
///     .FromConnectionString("kurrentdb://username:password@localhost:2113?tls=true")
///     .WithResilience(KurrentClientResilienceOptions.HighAvailability)
///     .Build();
/// </code>
/// </example>
[PublicAPI]
public sealed class KurrentClientOptionsBuilder : OptionsBuilder<KurrentClientOptionsBuilder, KurrentClientOptions> {
    public KurrentClientOptionsBuilder(KurrentClientOptions options) : base(options) { }
    public KurrentClientOptionsBuilder() : base(new KurrentClientOptions()) { }

    /// <summary>
    /// Builds the final KurrentClientOptions instance after applying all configurations and validating them.
    /// </summary>
    /// <returns>A configured KurrentClientOptions instance.</returns>
    protected override KurrentClientOptions OnBuild(KurrentClientOptions options) {
        options.EnsureOptionsAreValid();
        return options;

        // // might not need to do this anymore, double check.
        // return options with {
        //     Endpoints = options.Endpoints.ToArray(),
        //     Interceptors = options.Interceptors.ToArray()
        // };
    }

    /// <summary>
    /// Creates a new <see cref="KurrentClient"/> instance using the configured options.
    /// </summary>
    /// <returns></returns>
    public KurrentClient CreateClient() => KurrentClient.Create(Build());

    /// <summary>
    /// Creates a builder with default options.
    /// </summary>
    /// <returns>A new KurrentClientOptionsBuilder instance with default settings.</returns>
    public static KurrentClientOptionsBuilder Create() => new();

    /// <summary>
    /// Creates a builder initialized from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <returns>A new KurrentClientOptionsBuilder instance configured according to the connection string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
    /// <exception cref="ArgumentException">Thrown when connectionString is invalid or empty.</exception>
    /// <remarks>
    /// <para>
    /// Connection strings provide complete configuration snapshots and should be used as the
    /// starting point for builder-based configuration. Use this method to initialize the builder
    /// with a connection string, then apply incremental modifications with other builder methods.
    /// </para>
    /// <para>
    /// Connection strings cannot be applied mid-build as they would discard all accumulated
    /// configuration. Always start with a connection string when using one.
    /// </para>
    /// <para>
    /// Alternatively, use <see cref="KurrentClientOptions.Parse(string)"/> followed by
    /// <see cref="KurrentClientOptions.GetBuilder()"/> for the same result.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Start from connection string, then override specific settings
    /// var options = KurrentClientOptionsBuilder
    ///     .FromConnectionString("kurrentdb://admin:changeit@localhost:2113?tls=false")
    ///     .WithResilience(KurrentClientResilienceOptions.HighAvailability)
    ///     .ConfigureConnection(conn => conn.WithConnectionName("my-service"))
    ///     .Build();
    /// </code>
    /// </example>
    public static KurrentClientOptionsBuilder FromConnectionString(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new KurrentClientOptionsBuilder(KurrentClientOptions.Parse(connectionString));
    }

    public KurrentClientOptionsBuilder ConfigureConnection(Action<ConnectionOptionsBuilder> configure) =>
        WithBuilder(configure);

    /// <summary>
    /// Sets the resilience options.
    /// </summary>
    /// <param name="resilienceOptions">The resilience configuration options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method performs <b>complete replacement</b> of the Resilience property.
    /// All existing resilience settings (including Deadline, KeepAliveInterval, KeepAliveTimeout, and Retry)
    /// will be replaced with the provided options.
    /// </para>
    /// <para>
    /// To preserve existing settings while modifying specific properties, use
    /// <see cref="ConfigureResilience"/> instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Complete replacement - discards all previous resilience settings
    /// var options = builder.WithResilience(KurrentClientResilienceOptions.HighAvailability);
    ///
    /// // Incremental modification - preserves unspecified properties
    /// var options = builder.ConfigureResilience(res => res with { Deadline = TimeSpan.FromSeconds(60) });
    /// </code>
    /// </example>
    public KurrentClientOptionsBuilder WithResilience(KurrentClientResilienceOptions resilienceOptions) =>
        With(options => options with { Resilience = resilienceOptions });

    /// <summary>
    /// Configures the existing resilience options.
    /// </summary>
    /// <param name="configure">
    /// A function to configure the existing <see cref="KurrentClientResilienceOptions"/>.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method performs <b>incremental modification</b> of the Resilience property.
    /// The configure function receives the current resilience settings and can modify
    /// specific properties while preserving all others.
    /// </para>
    /// <para>
    /// This is the preferred method when you want to change only specific resilience settings
    /// without affecting the rest of the configuration.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Modify only the Deadline, preserve all other settings
    /// var options = builder.ConfigureResilience(res => res with {
    ///     Deadline = TimeSpan.FromMinutes(2)
    /// });
    ///
    /// // Modify multiple properties while preserving others
    /// var options = builder.ConfigureResilience(res => res with {
    ///     Deadline = TimeSpan.FromSeconds(60),
    ///     Retry = res.Retry with { MaxAttempts = 5 }
    /// });
    /// </code>
    /// </example>
    public KurrentClientOptionsBuilder ConfigureResilience(Func<KurrentClientResilienceOptions, KurrentClientResilienceOptions> configure) =>
        With(options => options with { Resilience = configure(options.Resilience) });

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
    /// <example>
    /// <code>
    /// // Configure message type mappings
    /// var options = builder.WithMessages(mapper => mapper
    ///     .Map&lt;UserCreated&gt;("user-created")
    ///     .Map&lt;UserUpdated&gt;("user-updated"));
    /// </code>
    /// </example>
    public KurrentClientOptionsBuilder WithMessages(Action<MessageMappingBuilder> configure) =>
        With(options => options with { Mapper = new MessageMappingBuilder().With(configure).Build() });

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
    /// <example>
    /// <code>
    /// // Use a pre-configured mapper
    /// var mapper = new MessageTypeMapper();
    /// mapper.Map&lt;UserCreated&gt;("user-created");
    /// mapper.Map&lt;UserUpdated&gt;("user-updated");
    ///
    /// var options = builder.WithMessages(mapper);
    /// </code>
    /// </example>
    public KurrentClientOptionsBuilder WithMessages(MessageTypeMapper mapper) =>
        With(options => options with { Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper)) });

    /// <summary>
    /// Sets the interceptors.
    /// </summary>
    /// <param name="interceptors">The gRPC interceptors to use.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder WithInterceptors(params Interceptor[] interceptors) =>
        With(options => options with { Interceptors = interceptors });

    /// <summary>
    /// Sets the metadata decoder.
    /// </summary>
    /// <param name="decoder">The metadata decoder to use.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder WithCustomMetadataDecoder(MetadataDecoder decoder) =>
        With(options => options with { MetadataDecoder = decoder });

    /// <summary>
    /// Sets the logger factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder WithLoggerFactory(ILoggerFactory loggerFactory) =>
        With(options => options with { LoggerFactory = loggerFactory });

    /// <summary>
    /// Sets the schema options.
    /// </summary>
    /// <param name="schemaOptions">The schema configuration options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder WithSchema(KurrentClientSchemaOptions schemaOptions) =>
        With(options => options with { Schema = schemaOptions });
}
