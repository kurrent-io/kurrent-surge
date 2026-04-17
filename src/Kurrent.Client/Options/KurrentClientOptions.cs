using System.ComponentModel;
using System.Net;
using System.Text.Json.Serialization;
using Grpc.Core.Interceptors;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Schema.Serialization.Protobuf;
using Kurrent.Client.Streams;
using Kurrent.Validation;
using KurrentDB.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static System.StringComparer;
using static System.TimeSpan;

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace Kurrent.Client;

/// <summary>
/// Configuration options for the Kurrent client.
/// </summary>
/// <remarks>
/// <para>
/// Provides comprehensive configuration for the Kurrent client, including connection settings,
/// security options, resilience strategies, and extensibility points.
/// </para>
/// </remarks>
[PublicAPI]
public record KurrentClientOptions : SelfValidating<KurrentClientOptions, KurrentClientOptions.Validator> {
    public static Builder Build => new();

    public static KurrentClientOptions Parse(string connectionString) =>
        KurrentConnectionString.Parse(connectionString).ToClientOptions();

    public Builder GetBuilder() => new(this);

    public static readonly string DefaultConnectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

    /// <summary>
    /// When options were created from a connection string, this property holds
    /// the connection string used to connect to the KurrentDB cluster.
    /// </summary>
    public string OriginalConnectionString { get; internal init; } = "";

    /// <summary>
    /// Indicates whether the original connection string was provided.
    /// </summary>
    public bool HasOriginalConnectionString => !string.IsNullOrWhiteSpace(OriginalConnectionString);

    /// <summary>
	/// The name of the connection.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A unique identifier for the connection, used in logging and diagnostics.
	/// </para>
	/// <para>
	/// Defaults to a randomly generated GUID-based name if not specified.
	/// </para>
	/// </remarks>
	public string ConnectionName { get; init; } = $"conn-{Guid.NewGuid()}";

    /// <summary>
    /// The URI scheme used for HTTP connections, based on the security settings.
    /// </summary>
    public string HttpUriScheme => Security.Transport.IsEnabled ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;

    /// <summary>
    /// The URI scheme used for the connection, based on the connection scheme.
    /// It can be either "kurrentdb" or "kurrentdb+discover".
    /// </summary>
    public string ConnectionUriScheme => ConnectionScheme.Description;

    /// <summary>
    /// Scheme used by the resolver for establishing connections to the cluster.
    /// It can be either "kurrentdb" for single-node connections or "kurrentdb+discover" for multi-node clusters with discovery.
    /// </summary>
    public KurrentConnectionScheme ConnectionScheme { get; init; } = KurrentConnectionScheme.Direct;


    /// <summary>
    /// Gets the address of the KurrentDB cluster based on the connection scheme and endpoints.
    /// </summary>
    public Uri Address {
        get {
            if (ConnectionScheme == KurrentConnectionScheme.Direct && Endpoints.Length == 1) {
                var directEndpoint = Endpoints[0];
                return new UriBuilder(HttpUriScheme, directEndpoint.Host, directEndpoint.Port).Uri;
            }

            // If we're using discovery, we don't have a direct address
            // we already validated that we must have endpoints
            return new($"{KurrentConnectionScheme.Discover.Description}://{Guid.NewGuid()}");
        }
    }

    /// <summary>
    /// List of <see cref="DnsEndPoint"/>s used to seed gossip.
    /// </summary>
    public DnsEndPoint[] Endpoints { get; init; } = [new("localhost", 2113)];

    /// <summary>
    /// Represents the configuration options for gossip-based discovery in the KurrentDB cluster.
    /// This property provides settings such as discovery intervals, timeouts, and preferences
    /// for selecting nodes during communication in the cluster.
    /// </summary>
    public KurrentClientGossipOptions Gossip { get; init; } = KurrentClientGossipOptions.Default;

    /// <summary>
    /// Security configuration for the client connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls transport security (TLS) and authentication credentials.
    /// </para>
    /// <para>
    /// Provides comprehensive security options for the connection, including
    /// transport layer security and client authentication.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new KurrentClientOptions {
    ///     Security = KurrentClientSecurityOptions.MutualTls(
    ///         clientCertPath: "/path/to/client.crt",
    ///         clientKeyPath: "/path/to/client.key",
    ///         rootCaPath: "/path/to/ca.crt"
    ///     )
    /// };
    /// </code>
    /// </example>
    public KurrentClientSecurityOptions Security { get; init; } = KurrentClientSecurityOptions.Default;

    /// <summary>
    /// Configuration for resilience strategies, including retries, hedging, and jitter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls how the client handles transient failures and recovery strategies.
    /// </para>
    /// </remarks>
    public KurrentClientResilienceOptions Resilience { get; init; } = KurrentClientResilienceOptions.NoResilience;

    /// <summary>
    /// Schema configuration options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls how the client handles event schemas and versioning.
    /// </para>
    /// </remarks>
    public KurrentClientSchemaOptions Schema { get; init; } = KurrentClientSchemaOptions.FullValidation;

	/// <summary>
	/// An optional list of <see cref="Interceptor"/>s to use.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Interceptors allow for inspection and modification of gRPC calls.
	/// </para>
	/// <para>
	/// Common uses include logging, metrics collection, and authentication.
	/// </para>
	/// </remarks>
	public IReadOnlyList<Interceptor> Interceptors { get; init; } = [];

	/// <summary>
	/// Metadata decoder for processing event metadata.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Responsible for decoding metadata attached to events.
	/// </para>
	/// </remarks>
	public MetadataDecoder MetadataDecoder { get; init; } = new DefaultMetadataDecoder();

    /// <summary>
    /// An optional <see cref="ILoggerFactory"/> to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The logger factory used to create loggers for the client.
    /// </para>
    /// <para>
    /// Defaults to <see cref="NullLoggerFactory.Instance"/> if not specified.
    /// </para>
    /// </remarks>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    public override string ToString() => this.GenerateConnectionString();

    public class Validator : Validator<Validator, KurrentClientOptions> {
        public Validator() {
            // Connection name validation
            RuleFor(options => options.ConnectionName)
                .NotEmpty()
                .WithMessage("Connection name cannot be empty.");

            RuleFor(options => options.ConnectionName.Length)
                .LessThanOrEqualTo(96)
                .WithMessage("Connection name exceeds 96 characters, which may impact readability in logs and diagnostics.");

            // Direct connection endpoint validation
            When(options => options.ConnectionScheme == KurrentConnectionScheme.Direct, () => {
                RuleFor(options => options.Endpoints.Length)
                    .Equal(1)
                    .WithMessage("Direct connection must have exactly one endpoint.");
            });

            // Discovery connection endpoints validation
            When(options => options.ConnectionScheme == KurrentConnectionScheme.Discover, () => {
                RuleFor(options => options.Endpoints.Length)
                    .GreaterThan(0)
                    .WithMessage("Discovery connection must have at least one endpoint.");

                RuleFor(options => options.Endpoints.Length)
                    .LessThanOrEqualTo(10)
                    .WithMessage("More than 10 endpoints provided for discovery connection. This may impact performance.")
                    .WithSeverity(RuleSeverity.Warning);
            });

            // Endpoint validation
            RuleForEach(options => options.Endpoints)
                .ChildRules(endpoint => {
                    endpoint.RuleFor(ep => ep.Host)
                        .NotEmpty()
                        .WithMessage("Endpoint host cannot be empty.");

                    endpoint.RuleFor(ep => ep.Host)
                        .Must(host => !host.Contains(' ') && !host.Contains('\t') && !host.Any(char.IsControl),
                            "Invalid hostname in endpoint.")
                        .WithMessage(ctx => $"Invalid hostname in endpoint: {ctx.Host}");

                    endpoint.RuleFor(ep => ep.Port)
                        .InclusiveBetween(1, 65535)
                        .WithMessage("Port must be between 1 and 65535.");
                });

            RuleFor(options => options.Gossip)
                .SetValidator(KurrentClientGossipOptions.Validator.Instance);

            RuleFor(options => options.Resilience)
                .SetValidator(KurrentClientResilienceOptions.Validator.Instance);

            // RuleFor(options => options.Schema)
            //     .SetValidator(KurrentClientSchemaOptions.Validator.Instance);

            RuleFor(options => options.Security)
                .SetValidator(KurrentClientSecurityOptions.Validator.Instance);
        }
    }

    /// <summary>
    /// Creates a new <see cref="KurrentClientOptions"/> instance from configuration.
    /// <remarks>
    /// The configuration is built automatically from standard sources including
    /// appsettings.json files and environment variables.
    /// </remarks>
    /// </summary>
    public static KurrentClientOptions FromConfiguration(string prefix = "kurrent:client") {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", true)
            .AddJsonFile($"appsettings.{environment}.json", true)                    // Accept default naming convention
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", true) // Linux is case-sensitive
            .AddEnvironmentVariables()
            .Build();

        // given the complexity of mapping configuration to options,
        // we might not be able to bind directly in all cases. So we try to bind first,
        // and if that fails, we fall back to default options.

        // this is because our Options are in fact settings that can be provided partially.
        // by this I mean that certain properties are complex types and implementations
        // that need to be constructed properly

        var options = configuration.Get<KurrentClientOptions>() ?? new KurrentClientOptions();

        return options;
    }

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
    /// var options = KurrentClientOptions.Builder.Create()
    ///     .ConfigureConnection(builder => builder
    ///         .WithConnectionName("my-service")
    ///         .WithDirectConnection("kurrentdb.example.com", 2113)
    ///         .ConfigureSecurity(KurrentClientSecurityOptions.MutualTls(
    ///             clientCertPath: "/path/to/client.crt",
    ///             clientKeyPath: "/path/to/client.key")))
    ///     .Build();
    ///
    /// // Create from connection string
    /// var options = KurrentClientOptions.Builder
    ///     .FromConnectionString("kurrentdb://username:password@localhost:2113?tls=true")
    ///     .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    ///     .Build();
    /// </code>
    /// </example>
    [PublicAPI]
    public sealed class Builder(KurrentClientOptions options) : FluentBuilder<Builder, KurrentClientOptions>(options) {
        public Builder() : this(new()) { }

        /// <summary>
        /// Builds the final KurrentClientOptions instance after applying all configurations and validating them.
        /// </summary>
        /// <returns>A configured KurrentClientOptions instance.</returns>
        protected override KurrentClientOptions OnBuild(KurrentClientOptions options) {
            options.EnsureValid();
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
        /// <returns>A new Builder instance with default settings.</returns>
        public static Builder Create() => new();

        /// <summary>
        /// Creates a builder initialized from a connection string.
        /// </summary>
        /// <param name="connectionString">The connection string to parse.</param>
        /// <returns>A new Builder instance configured according to the connection string.</returns>
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
        public static Builder FromConnectionString(string connectionString) {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            return new(KurrentClientOptions.Parse(connectionString));
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
        public Builder FromConfiguration(
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

        public Builder ConfigureConnection(Action<ConnectionBuilder> configure) =>
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
        public Builder ConfigureResilience(KurrentClientResilienceOptions resilienceOptions) =>
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
        public Builder ConfigureResilience(Func<KurrentClientResilienceOptions, KurrentClientResilienceOptions> configure) =>
            With(options => options with { Resilience = configure(options.Resilience) });

        /// <summary>
        /// Sets the interceptors.
        /// </summary>
        /// <param name="interceptors">The gRPC interceptors to use.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public Builder WithInterceptors(params Interceptor[] interceptors) =>
            With(options => options with { Interceptors = interceptors });

        /// <summary>
        /// Sets the metadata decoder.
        /// </summary>
        /// <param name="decoder">The metadata decoder to use.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public Builder WithCustomMetadataDecoder(MetadataDecoder decoder) =>
            With(options => options with { MetadataDecoder = decoder });

        /// <summary>
        /// Sets the logger factory.
        /// </summary>
        /// <param name="loggerFactory">The logger factory to use.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public Builder WithLoggerFactory(ILoggerFactory loggerFactory) =>
            With(options => options with { LoggerFactory = loggerFactory });

        /// <summary>
        /// Configures the schema options.
        /// </summary>
        /// <param name="schemaOptions">The schema configuration options.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public Builder ConfigureSchema(KurrentClientSchemaOptions schemaOptions) =>
            With(options => options with { Schema = schemaOptions });

        /// <summary>
        /// Configures the schema options using a sub-builder.
        /// </summary>
        /// <param name="configure">Action to configure the schema options builder.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public Builder ConfigureSchema(Action<SchemaBuilder> configure) =>
            WithBuilder(configure);
    }

    [PublicAPI]
    public class ConnectionBuilder(KurrentClientOptions options) : FluentBuilder<ConnectionBuilder, KurrentClientOptions>(options) {
        public ConnectionBuilder() : this(new()) { }

        /// <summary>
        /// Sets the connection name.
        /// </summary>
        /// <param name="connectionName">The name of the connection.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithConnectionName(string connectionName) {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
            return With(options => options with { ConnectionName = connectionName });
        }

        /// <summary>
        /// Sets the connection scheme to direct and configures a single-node connection.
        /// <remarks>
        /// Overrides any existing endpoints and sets the connection scheme to <see cref="KurrentConnectionScheme.Direct"/>.
        /// </remarks>
        /// </summary>
        /// <param name="uri">The URI of the server.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithDirectConnection(Uri uri) =>
            WithDirectConnection(uri.Host, uri.Port, uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Sets the connection scheme to direct and configures a single-node connection.
        /// <remarks>
        /// Overrides any existing endpoints and sets the connection scheme to <see cref="KurrentConnectionScheme.Direct"/>.
        /// </remarks>
        /// </summary>
        /// <param name="host">The hostname of the server.</param>
        /// <param name="port">The port of the server.</param>
        /// <param name="useTls">Whether to use TLS.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithDirectConnection(string host, int port, bool useTls = true) {
            return With(options => options with {
                ConnectionScheme = KurrentConnectionScheme.Direct,
                Endpoints        = [new(host, port)],
                Security         = options.Security with { Transport = useTls ? TransportSecurity.Standard : TransportSecurity.None }
            });
        }

        /// <summary>
        /// Sets the connection scheme to discover and configures a gossip endpoint for cluster discovery.
        /// </summary>
        /// <param name="uri">The URI of the server.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithGossipEndpoint(Uri uri) =>
            WithGossipEndpoint(uri.Host, uri.Port, uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Sets the connection scheme to discover and configures a gossip endpoint for cluster discovery.
        /// </summary>
        /// <param name="host">The hostname of the server.</param>
        /// <param name="port">The port of the server.</param>
        /// <param name="useTls">Whether to use TLS.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithGossipEndpoint(string host, int port, bool useTls = true) =>
            WithGossipEndpoint(new DnsEndPoint(host, port));

        /// <summary>
        /// Sets the connection scheme to discover and configures a gossip endpoint for cluster discovery.
        /// </summary>
        /// <param name="endpoint">The DNS endpoint to use for gossip discovery.</param>
        /// <param name="useTls">Whether to use TLS.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithGossipEndpoint(DnsEndPoint endpoint, bool useTls = true) {
            return With(options => options with {
                ConnectionScheme = KurrentConnectionScheme.Discover,
                Endpoints        = options.Endpoints.Concat([endpoint]).ToArray(),
                Security         = options.Security with { Transport = useTls ? TransportSecurity.Standard : TransportSecurity.None }
            });
        }

        /// <summary>
        /// Sets the connection scheme to discover and configures multiple gossip endpoints for cluster discovery.
        /// </summary>
        /// <param name="endpoints">The DNS endpoints to use.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithGossipEndpoint(params DnsEndPoint[] endpoints) {
            return With(options => options with {
                ConnectionScheme = KurrentConnectionScheme.Discover,
                Endpoints        = options.Endpoints.Concat(endpoints).ToArray(),
                Security         = options.Security with { Transport = TransportSecurity.Standard }
            });
        }

        /// <summary>
        /// Configures the gossip options.
        /// </summary>
        /// <param name="gossipOptions">The gossip configuration options.</param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder ConfigureGossip(KurrentClientGossipOptions gossipOptions) =>
            With(options => options with { Gossip = gossipOptions });

        /// <summary>
        /// Configures the existing gossip options.
        /// </summary>
        /// <param name="configure">
        /// A function to configure the existing <see cref="KurrentClientGossipOptions"/>.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder ConfigureGossip(Func<KurrentClientGossipOptions, KurrentClientGossipOptions> configure) =>
            With(options => options with { Gossip = configure(options.Gossip) });

        /// <summary>
        /// Sets the authentication credentials for the client.
        /// </summary>
        /// <param name="credentials">
        /// The client credentials to use for authentication.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithBasicAuthentication(ClientCredentials credentials) =>
            With(options => options with { Security = options.Security with { Authentication = credentials } });

        /// <summary>
        /// Sets the authentication credentials for the client using basic authentication.
        /// </summary>
        /// <param name="username">
        /// The username for basic authentication.
        /// </param>
        /// <param name="password">
        /// The password for basic authentication. Can be null if no password is required.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithBasicAuthentication(string username, string? password) =>
            WithBasicAuthentication(ClientCredentials.Basic(username, password));

        /// <summary>
        /// Sets the authentication credentials for the client using basic authentication.
        /// </summary>
        /// <param name="username">
        /// The username for basic authentication.
        /// </param>
        /// <param name="password">
        /// The password for basic authentication. Can be null if no password is required.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithCertificateAuthentication(string username, string? password) =>
            WithBasicAuthentication(ClientCredentials.Basic(username, password));

        /// <summary>
        /// Sets the transport security configuration.
        /// </summary>
        /// <param name="transportSecurity">
        /// The transport security configuration to use.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithTransportSecurity(TransportSecurity transportSecurity) =>
            With(options => options with { Security = options.Security with { Transport = transportSecurity } });

        /// <summary>
        /// Configures the client to use insecure transport (no TLS).
        /// </summary>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder WithInsecureTransport() =>
            With(options => options with { Security = options.Security with { Transport = TransportSecurity.Insecure } });

        /// <summary>
        /// Configures the security options.
        /// </summary>
        /// <param name="securityOptions">
        /// The security options to set. If provided, this will override the existing security options.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder ConfigureSecurity(KurrentClientSecurityOptions securityOptions) =>
            With(options => options with { Security = securityOptions });

        /// <summary>
        /// Configures the existing security options.
        /// </summary>
        /// <param name="configure">
        /// A function to configure the existing <see cref="KurrentClientSecurityOptions"/>.
        /// </param>
        /// <returns>The builder instance for method chaining.</returns>
        public ConnectionBuilder ConfigureSecurity(Func<KurrentClientSecurityOptions, KurrentClientSecurityOptions> configure) =>
            With(options => options with { Security = configure(options.Security) });
    }

    [PublicAPI]
    public class SchemaBuilder(KurrentClientOptions options) : FluentBuilder<SchemaBuilder, KurrentClientOptions>(options) {
        public SchemaBuilder() : this(new()) { }

        /// <summary>
        /// Configures the JSON serializer options using a sub-builder.
        /// </summary>
        public SchemaBuilder ConfigureJson(Action<JsonSerializerBuilder> configure) =>
            WithBuilder(configure);

        /// <summary>
        /// Configures the Protobuf serializer options using a sub-builder.
        /// </summary>
        public SchemaBuilder ConfigureProtobuf(Action<ProtobufSerializerBuilder> configure) =>
            WithBuilder(configure);

        /// <summary>
        /// Sets whether schemas should be automatically registered.
        /// </summary>
        public SchemaBuilder WithAutoRegister(bool autoRegister) =>
            With(opts => opts with { Schema = opts.Schema with { AutoRegister = autoRegister } });

        /// <summary>
        /// Sets whether messages should be validated against their schemas.
        /// </summary>
        public SchemaBuilder WithValidation(bool validate) =>
            With(opts => opts with { Schema = opts.Schema with { PreValidate = validate } });

        /// <summary>
        /// Sets the strategy used for generating schema names.
        /// </summary>
        public SchemaBuilder WithNameStrategy(ISchemaNameStrategy nameStrategy) =>
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
        public SchemaBuilder ConfigureMessages(Action<MessageTypeMapper.Builder> configure) =>
            With(opts => opts with { Schema = opts.Schema with { Mapper = new MessageTypeMapper.Builder().With(configure).Build() } });

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
        public SchemaBuilder ConfigureMessages(MessageTypeMapper mapper) =>
            With(opts => opts with { Schema = opts.Schema with { Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper)) } });
    }

    [PublicAPI]
    public class JsonSerializerBuilder(KurrentClientOptions options) : FluentBuilder<JsonSerializerBuilder, KurrentClientOptions>(options) {
        public JsonSerializerBuilder() : this(new()) { }

        /// <summary>
        /// Sets the user-provided <see cref="JsonSerializerContext"/> instances for AOT-compatible
        /// JSON serialization of domain types.
        /// </summary>
        public JsonSerializerBuilder WithContexts(params JsonSerializerContext[] contexts) =>
            With(opts => opts with {
                Schema = opts.Schema with {
                    Json = opts.Schema.Json with { UserJsonContexts = contexts }
                }
            });

        /// <summary>
        /// Sets whether the protobuf formatter should be used for JSON serialization.
        /// </summary>
        public JsonSerializerBuilder WithProtobufFormatter(bool useProtobufFormatter) =>
            With(opts => opts with {
                Schema = opts.Schema with {
                    Json = opts.Schema.Json with { UseProtobufFormatter = useProtobufFormatter }
                }
            });
    }

    [PublicAPI]
    public class ProtobufSerializerBuilder(KurrentClientOptions options) : FluentBuilder<ProtobufSerializerBuilder, KurrentClientOptions>(options) {
        public ProtobufSerializerBuilder() : this(new()) { }

        /// <summary>
        /// Sets the <see cref="ProtoSerializerContext"/> for AOT-compatible protobuf deserialization.
        /// </summary>
        public ProtobufSerializerBuilder WithContext(ProtoSerializerContext context) =>
            With(opts => opts with {
                Schema = opts.Schema with {
                    Protobuf = opts.Schema.Protobuf with { Context = context }
                }
            });
    }
}

static class KurrentDBConnectionStringExtensions {
    static T GetParameterValue<T>(Dictionary<string, string> source, string key, T defaultValue) =>
        !source.TryGetValue(key, out var strValue) ? defaultValue : (T)Convert.ChangeType(strValue, typeof(T));

    static string? GetParameterValue(Dictionary<string, string> source, string key, string? defaultValue) =>
        !source.TryGetValue(key, out var value) ? defaultValue : value;

    static bool GetParameterValue(Dictionary<string, string> source, string key, bool defaultValue) =>
        !source.TryGetValue(key, out var strValue) ? defaultValue : bool.TryParse(strValue, out var value) ? value : defaultValue;

    /// <param name="source">The V1 connection string configuration to convert</param>
    extension(KurrentConnectionString source) {
        /// <summary>
        /// Converts a V1 connection string configuration to a V2 client options instance.
        /// </summary>
        /// <returns>A new KurrentClientOptions instance configured based on the connection string</returns>
        /// <remarks>
        /// <para>
        /// This method enables seamless migration from V1 to V2 of the KurrentDB client by
        /// preserving connection settings and security configuration across versions.
        /// </para>
        /// <para>
        /// All resilience settings from the connection string are mapped to the equivalent
        /// resilience options in the V2 client.
        /// </para>
        /// </remarks>
        public KurrentClientOptions ToClientOptions() {
            var connectionName = GetParameterValue<string>(source.Options, "ConnectionName", $"conn-{Guid.NewGuid():D}");

            var schemeType = KurrentConnectionString.DiscoverySchemes.Contains(source.Scheme, OrdinalIgnoreCase)
                ? KurrentConnectionScheme.Discover
                : KurrentConnectionScheme.Direct;

            var options =  new KurrentClientOptions {
                ConnectionName           = connectionName,
                ConnectionScheme         = schemeType,
                Endpoints                = source.Hosts,
                Security                 = ConfigureSecurityOptions(),
                Resilience               = ConfigureResilienceOptions(),
                Gossip                   = ConfigureGossipOptions(),
                OriginalConnectionString = source.ConnectionString
            };

            return options;

            KurrentClientSecurityOptions ConfigureSecurityOptions() {
                return new() {
                    Transport      = ConfigureTransportSecurity(),
                    Authentication = ConfigureAuthentication()
                };

                TransportSecurity ConfigureTransportSecurity() {
                    var useTls        = GetParameterValue(source.Options, "Tls", true);
                    var tlsVerifyCert = GetParameterValue(source.Options, "TlsVerifyCert", true);
                    var tlsCaFile     = GetParameterValue(source.Options, "TlsCaFile", null);

                    return !useTls
                        ? TransportSecurity.None
                        : tlsCaFile is not null
                            ? TransportSecurity.Certificate(tlsCaFile)
                            : tlsVerifyCert
                                ? TransportSecurity.Standard
                                : TransportSecurity.Insecure;
                }

                ClientCredentials ConfigureAuthentication() {
                    var userCertFile = GetParameterValue(source.Options, "UserCertFile", null);
                    var userKeyFile  = GetParameterValue(source.Options, "UserKeyFile", null);

                    return !string.IsNullOrEmpty(userCertFile) && !string.IsNullOrEmpty(userKeyFile)
                        ? ClientCredentials.Certificate(userCertFile, userKeyFile)
                        : source.UserInfo is not null
                            ? ClientCredentials.Basic(source.UserInfo.Value.User, source.UserInfo.Value.Password)
                            : ClientCredentials.None;
                }
            }

            KurrentClientResilienceOptions ConfigureResilienceOptions() {
                var keepAliveIntervalMs = GetParameterValue(source.Options, "KeepAliveInterval", 60000);
                var keepAliveTimeoutMs  = GetParameterValue(source.Options, "KeepAliveTimeout", 30000);
                var defaultDeadlineMs   = GetParameterValue(source.Options, "DefaultDeadline", 30000);

                var keepAliveInterval = keepAliveIntervalMs == -1 ? Timeout.InfiniteTimeSpan : FromMilliseconds(keepAliveIntervalMs);
                var keepAliveTimeout  = keepAliveTimeoutMs  == -1 ? Timeout.InfiniteTimeSpan : FromMilliseconds(keepAliveTimeoutMs);
                var deadline          = defaultDeadlineMs   == -1 ? Timeout.InfiniteTimeSpan : FromMilliseconds(defaultDeadlineMs);

                return new() {
                    KeepAliveInterval = keepAliveInterval,
                    KeepAliveTimeout  = keepAliveTimeout,
                    Deadline          = deadline
                };
            }

            KurrentClientGossipOptions ConfigureGossipOptions() {
                var maxDiscoverAttempts = GetParameterValue(source.Options, "MaxDiscoverAttempts", 10);
                var discoveryInterval   = GetParameterValue(source.Options, "DiscoveryInterval", 100);
                var gossipTimeout       = GetParameterValue(source.Options, "GossipTimeout", 5000);
                var nodePreference      = GetParameterValue(source.Options, "NodePreference", NodePreference.Random);

                return new() {
                    MaxDiscoverAttempts = maxDiscoverAttempts,
                    DiscoveryInterval   = FromMilliseconds(discoveryInterval),
                    Timeout             = FromMilliseconds(gossipTimeout),
                    ReadPreference      = nodePreference
                };
            }
        }
    }
}

/// <summary>
/// Represents the connection scheme used by the Kurrent client.
/// </summary>
public enum KurrentConnectionScheme {
    /// <summary>Direct connection to a single node</summary>
    [Description("kurrentdb")]
    Direct,

    /// <summary>Cluster connection with discovery via gossip protocol</summary>
    [Description("kurrentdb+discover")]
    Discover
}
