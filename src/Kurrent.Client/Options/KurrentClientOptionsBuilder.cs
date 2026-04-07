#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

using Grpc.Core.Interceptors;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Configuration;
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
///         .WithDirectConnection("kurrentdb.example.com", 2113)
///         .ConfigureSecurity(KurrentClientSecurityOptions.MutualTls(
///             clientCertPath: "/path/to/client.crt",
///             clientKeyPath: "/path/to/client.key")))
///     .Build();
///
/// // Create from connection string
/// var options = KurrentClientOptionsBuilder
///     .FromConnectionString("kurrentdb://username:password@localhost:2113?tls=true")
///     .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
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
    public static KurrentClientOptionsBuilder FromConnectionString(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new KurrentClientOptionsBuilder(KurrentClientOptions.Parse(connectionString));
    }

    /// <summary>
    /// Loads client options from an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="sectionName">The section name under <c>Kurrent</c>. Defaults to "Client".</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Configuration is applied in layers:
    /// </para>
    /// <list type="number">
    /// <item><c>ConnectionString</c> is parsed first to establish base settings (endpoints, auth, TLS)</item>
    /// <item><c>Resilience</c>, <c>Gossip</c>, and <c>Schema</c> sections override the parsed values</item>
    /// <item>Fluent builder methods can further override any setting</item>
    /// </list>
    /// </remarks>
    public KurrentClientOptionsBuilder FromConfiguration(
        IConfiguration configuration,
        string sectionName = "Client") {
        var sectionPath = $"Kurrent:{sectionName}";
        var section     = configuration.GetSection(sectionPath);

        if (!section.Exists()) return this;

        // 1. Parse ConnectionString first (if present) as base
        var connectionString = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString)) {
            var baseOptions = KurrentClientOptions.Parse(connectionString);
            With(_ => baseOptions);
        }

        // 2. Bind individual nested sections as overrides
        var resilienceSection = section.GetSection("Resilience");
        if (resilienceSection.Exists()) {
            var resilience = resilienceSection.Get<KurrentClientResilienceOptions>();
            if (resilience is not null)
                ConfigureResilience(_ => resilience);
        }

        var gossipSection = section.GetSection("Gossip");
        if (gossipSection.Exists()) {
            var gossip = gossipSection.Get<KurrentClientGossipOptions>();
            if (gossip is not null)
                With(opts => opts with { Gossip = gossip });
        }

        var schemaSection = section.GetSection("Schema");
        if (schemaSection.Exists()) {
            var schema = schemaSection.Get<KurrentClientSchemaOptions>();
            if (schema is not null)
                With(opts => opts with { Schema = schema });
        }

        // 3. Bind ConnectionName if present (overrides connection string value)
        var connectionName = section["ConnectionName"];
        if (!string.IsNullOrWhiteSpace(connectionName))
            ConfigureConnection(conn => conn.WithConnectionName(connectionName));

        return this;
    }

    public KurrentClientOptionsBuilder ConfigureConnection(Action<ConnectionOptionsBuilder> configure) =>
        WithBuilder(configure);

    /// <summary>
    /// Configures the resilience options.
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
    /// the <c>ConfigureResilience</c> overload that takes a Func instead.
    /// </para>
    /// </remarks>
    public KurrentClientOptionsBuilder ConfigureResilience(KurrentClientResilienceOptions resilienceOptions) =>
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
    public KurrentClientOptionsBuilder ConfigureResilience(Func<KurrentClientResilienceOptions, KurrentClientResilienceOptions> configure) =>
        With(options => options with { Resilience = configure(options.Resilience) });

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
    /// Configures the schema options.
    /// </summary>
    /// <param name="schemaOptions">The schema configuration options.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder ConfigureSchema(KurrentClientSchemaOptions schemaOptions) =>
        With(options => options with { Schema = schemaOptions });

    /// <summary>
    /// Configures the schema options using a sub-builder.
    /// </summary>
    /// <param name="configure">Action to configure the schema options builder.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public KurrentClientOptionsBuilder ConfigureSchema(Action<SchemaOptionsBuilder> configure) =>
        WithBuilder(configure);
}
