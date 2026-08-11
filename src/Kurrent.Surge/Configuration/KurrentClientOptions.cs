using System.ComponentModel;
using System.Net;
using System.Text;
using Kurrent.Surge.Client.Streams;
using Kurrent.Validation;
using KurrentDB.Client;
using static System.StringComparer;
using static System.TimeSpan;

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace Kurrent.Surge.Client;

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
public record KurrentClientOptions : SelfValidating<KurrentClientOptions, KurrentClientOptions.Validator>, IKurrentClientOptions {
    public static KurrentClientOptions Parse(string connectionString) =>
        KurrentConnectionString.Parse(connectionString).ToClientOptions();

    public static readonly string DefaultConnectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

    /// <summary>
    /// When options were created from a connection string, this property holds
    /// the connection string used to connect to the KurrentDB cluster.
    /// </summary>
    public string OriginalConnectionString { get; internal set; } = "";

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
	public string ConnectionName { get; set; } = $"conn-{Guid.NewGuid()}";

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
    public KurrentConnectionScheme ConnectionScheme { get; set; } = KurrentConnectionScheme.Direct;

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
    public DnsEndPoint[] Endpoints { get; set; } = [new("localhost", 2113)];

    /// <summary>
    /// Represents the configuration options for gossip-based discovery in the KurrentDB cluster.
    /// This property provides settings such as discovery intervals, timeouts, and preferences
    /// for selecting nodes during communication in the cluster.
    /// </summary>
    public KurrentClientGossipOptions Gossip { get; set; } = KurrentClientGossipOptions.Default;

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
    public KurrentClientSecurityOptions Security { get; set; } = KurrentClientSecurityOptions.Tls;

    /// <summary>
    /// Configuration for resilience strategies, including retries, hedging, and jitter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls how the client handles transient failures and recovery strategies.
    /// </para>
    /// </remarks>
    public KurrentClientResilienceOptions Resilience { get; set; } = KurrentClientResilienceOptions.NoResilience;

    /// <summary>
    /// Schema configuration options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls how the client handles event schemas and versioning.
    /// </para>
    /// </remarks>
    public KurrentClientSchemaOptions Schema { get; set; } = KurrentClientSchemaOptions.FullValidation;

    /// <summary>
    /// Compression configuration for outgoing gRPC calls.
    /// </summary>
    /// <remarks>
    /// Disabled by default. See <see cref="KurrentClientCompressionOptions"/>.
    /// </remarks>
    public KurrentClientCompressionOptions Compression { get; set; } = KurrentClientCompressionOptions.Default;

	/// <summary>
	/// Metadata decoder for processing event metadata.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Responsible for decoding metadata attached to events.
	/// </para>
	/// </remarks>
	public IMetadataDecoder MetadataDecoder { get; set; } = new DefaultMetadataDecoder();

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
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    public KurrentClientMessageOptions Messages { get; private set; } = new();

    /// <summary>
    /// Configures message handling in place. Deliberately NOT `with`-based: options are
    /// configured through <c>Action&lt;KurrentClientOptions&gt;</c> callbacks where a cloned
    /// record would be silently discarded, losing the configuration.
    /// </summary>
    public KurrentClientOptions ConfigureMessages(Action<KurrentClientMessageOptions> configure) {
        configure(Messages);
        return this;
    }

    /// <summary>
    /// Applies a connection string to these options in place. Like
    /// <see cref="ConfigureMessages"/>, deliberately NOT `with`-based so it works inside
    /// <c>Action&lt;KurrentClientOptions&gt;</c> configuration callbacks.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., <c>kurrentdb://localhost:2113</c>).
    /// </param>
    /// <returns>These options for method chaining.</returns>
    public KurrentClientOptions Connection(string connectionString) {
        var parsed = Parse(connectionString);

        ConnectionScheme         = parsed.ConnectionScheme;
        Endpoints                = parsed.Endpoints;
        Security                 = parsed.Security;
        Resilience               = parsed.Resilience;
        Gossip                   = parsed.Gossip;
        OriginalConnectionString = parsed.OriginalConnectionString;

        // Parse generates a random "conn-" name when the string carries no connectionName
        // parameter — same heuristic as GenerateConnectionString. Only adopt explicit names
        // so a name set before this call survives.
        if (!parsed.ConnectionName.StartsWith("conn-"))
            ConnectionName = parsed.ConnectionName;

        return this;
    }

    public override string ToString() => GenerateConnectionString(this);

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

            RuleFor(options => options.Compression)
                .SetValidator(KurrentClientCompressionOptions.Validator.Instance);
        }
    }

    /// <summary>
    /// Converts the current options to a valid KurrentDB connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generates a connection string that represents these client options, matching the format expected by KurrentDB.
    /// For X509Certificate2, we can't extract file paths, so we skip these parameters bu add instead `userCert=:from_memory:` or `tlsCa=:from_memory:`.
    /// </para>
    /// </remarks>
    /// <returns>A connection string that can be used to connect to KurrentDB.</returns>
    static string GenerateConnectionString(KurrentClientOptions options) {
        var builder = new StringBuilder();

        // Add scheme
        builder.Append(options.ConnectionUriScheme);
        builder.Append("://");

        // Add authentication if present
        if (options.Security.Authentication.Value is BasicCredentials credentials) {
            builder.Append(Uri.EscapeDataString(credentials.Username));

            if (!string.IsNullOrEmpty(credentials.Password)) {
                builder.Append(':');
                builder.Append(Uri.EscapeDataString(credentials.Password));
            }

            builder.Append('@');
        }

        // if (options.Security.Authentication.Value is BasicUserCredentials credentials) {
        //     var credentialsBuilder = new StringBuilder();
        //     credentialsBuilder.Append(credentials.Username);
        //
        //     if (!string.IsNullOrEmpty(credentials.Password)) {
        //         credentialsBuilder.Append(':');
        //         credentialsBuilder.Append(credentials.Password);
        //     }
        //
        //     var final = Uri.EscapeDataString(credentialsBuilder.ToString());
        //     builder.Append(final);
        //     builder.Append('@');
        // }

        // Add endpoints
        if (options.Endpoints.Length > 0) {
            var endpoints = string.Join(",", options.Endpoints.Select(ep => $"{ep.Host}:{ep.Port}"));
            builder.Append(endpoints);
        }

        // Add query parameters
        var parameters = new List<string>();

        // Transport security parameters
        options.Security.Transport.Switch(
            _   => parameters.Add("tls=false"),
            tls  => {
                if (!tls.VerifyServerCertificate) parameters.Add("tlsVerifyCert=false");
            },
            file => {
                if (!file.VerifyServerCertificate) parameters.Add("tlsVerifyCert=false");
                parameters.Add($"tlsCaFile={Uri.EscapeDataString(file.CaPath)}");
            },
            x509 => {
                if (!x509.VerifyServerCertificate) parameters.Add("tlsVerifyCert=false");
                parameters.Add("tlsCa=:from_memory:");
            }
        );

        // Certificate authentication parameters
        switch (options.Security.Authentication.Value) {
            case FileCertificateCredentials certFileCredentials:
                parameters.Add($"userCertFile={Uri.EscapeDataString(certFileCredentials.CertificatePath)}");
                parameters.Add($"userKeyFile={Uri.EscapeDataString(certFileCredentials.KeyPath)}");
                break;

            case X509CertificateCredentials:
                parameters.Add("userCert=:from_memory:");
                break;
        }

        // Connection name parameter (only if not default)
        if (!string.IsNullOrEmpty(options.ConnectionName) && !options.ConnectionName.StartsWith("conn-"))
            parameters.Add($"connectionName={Uri.EscapeDataString(options.ConnectionName)}");

        // Gossip parameters
        if (options.Gossip.ReadPreference != NodeReadPreference.Random)
            parameters.Add($"nodePreference={options.Gossip.ReadPreference.ToString().ToLowerInvariant()}");

        if (options.Gossip.MaxDiscoverAttempts != KurrentClientGossipOptions.Default.MaxDiscoverAttempts)
            parameters.Add($"maxDiscoverAttempts={options.Gossip.MaxDiscoverAttempts}");

        if (options.Gossip.DiscoveryInterval != KurrentClientGossipOptions.Default.DiscoveryInterval)
            parameters.Add($"discoveryInterval={(int)options.Gossip.DiscoveryInterval.TotalMilliseconds}");

        if (options.Gossip.Timeout != KurrentClientGossipOptions.Default.Timeout)
            parameters.Add($"gossipTimeout={(int)options.Gossip.Timeout.TotalSeconds}");

        // Resilience parameters
        if (options.Resilience.KeepAliveInterval != KurrentClientResilienceOptions.Default.KeepAliveInterval &&
            options.Resilience.KeepAliveInterval != Timeout.InfiniteTimeSpan)
            parameters.Add($"keepAliveInterval={(int)options.Resilience.KeepAliveInterval.TotalSeconds}");

        if (options.Resilience.KeepAliveTimeout != KurrentClientResilienceOptions.Default.KeepAliveTimeout &&
            options.Resilience.KeepAliveTimeout != Timeout.InfiniteTimeSpan)
            parameters.Add($"keepAliveTimeout={(int)options.Resilience.KeepAliveTimeout.TotalSeconds}");

        if (options.Resilience.Deadline.HasValue &&
            options.Resilience.Deadline.Value != Timeout.InfiniteTimeSpan)
            parameters.Add($"defaultDeadline={(int)options.Resilience.Deadline.Value.TotalMilliseconds}");

        // Append query parameters if any
        if (parameters.Count > 0) {
            builder.Append('?');
            builder.Append(string.Join("&", parameters));
        }

        return builder.ToString();
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

file static class Extensions {
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
                    var userCertFile    = GetParameterValue(source.Options, "UserCertFile", null);
                    var userKeyFile     = GetParameterValue(source.Options, "UserKeyFile", null);
                    var userKeyPassword = GetParameterValue(source.Options, "UserKeyPassword", null);

                    return !string.IsNullOrEmpty(userCertFile) && !string.IsNullOrEmpty(userKeyFile)
                        ? ClientCredentials.Certificate(userCertFile, userKeyFile, userKeyPassword)
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

                // ReadPreference is the name going forward across every SDK; NodePreference is
                // the current cross-SDK spelling every client still documents and emits. Accept
                // both, ReadPreference wins if a connection string somehow carries both.
                var readPreference = GetParameterValue(
                    source.Options, "ReadPreference",
                    GetParameterValue(source.Options, "NodePreference", NodeReadPreference.Random));

                return new() {
                    MaxDiscoverAttempts = maxDiscoverAttempts,
                    DiscoveryInterval   = FromMilliseconds(discoveryInterval),
                    Timeout             = FromMilliseconds(gossipTimeout),
                    ReadPreference      = readPreference
                };
            }
        }
    }

    static T GetParameterValue<T>(Dictionary<string, string> source, string key, T defaultValue) {
        if (!source.TryGetValue(key, out var strValue))
            return defaultValue;

        return typeof(T).IsEnum
            ? (T)Enum.Parse(typeof(T), strValue, ignoreCase: true)
            : (T)Convert.ChangeType(strValue, typeof(T));
    }

    static string? GetParameterValue(Dictionary<string, string> source, string key, string? defaultValue) =>
        !source.TryGetValue(key, out var value) ? defaultValue : value;

    static bool GetParameterValue(Dictionary<string, string> source, string key, bool defaultValue) =>
        !source.TryGetValue(key, out var strValue) ? defaultValue : bool.TryParse(strValue, out var value) ? value : defaultValue;
}

