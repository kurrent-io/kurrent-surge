using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Client;

class KurrentDBClientSettings {
    /// <summary>
    /// The name of the connection.
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// An optional list of <see cref="Interceptor"/>s to use.
    /// </summary>
    public IEnumerable<Interceptor> Interceptors { get; set; } = [];

    /// <summary>
    /// An optional <see cref="HttpMessageHandler"/> factory.
    /// </summary>
    public Func<HttpMessageHandler>? CreateHttpMessageHandler { get; set; }

    /// <summary>
    /// An optional <see cref="ILoggerFactory"/> to use.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>
    /// The optional <see cref="ChannelCredentials"/> to use when creating the <see cref="ChannelBase"/>.
    /// </summary>
    public ChannelCredentials? ChannelCredentials { get; set; }

    /// <summary>
    /// The default <see cref="KurrentDBClientOperationOptions"/> to use.
    /// </summary>
    public KurrentDBClientOperationOptions OperationOptions { get; set; } =
        KurrentDBClientOperationOptions.Default;

    /// <summary>
    /// The <see cref="KurrentDBClientConnectivitySettings"/> to use.
    /// </summary>
    public KurrentDBClientConnectivitySettings ConnectivitySettings { get; set; } =
        KurrentDBClientConnectivitySettings.Default;

    /// <summary>
    /// The <see cref="UserCredentials"/> to use if none have been supplied to the operation.
    /// Defaults to <see cref="UserCredentials.Empty"/> for anonymous access.
    /// </summary>
    public UserCredentials DefaultCredentials { get; set; } = UserCredentials.Empty;

    /// <summary>
    /// The default deadline for calls. Will not be applied to reads or subscriptions.
    /// </summary>
    public TimeSpan? DefaultDeadline { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The retry settings to use for gRPC operations.
    /// </summary>
    public KurrentDBClientRetrySettings RetrySettings { get; set; } = KurrentDBClientRetrySettings.NoRetry;

    /// <summary>
    /// Creates client settings from a connection string
    /// </summary>
    /// <param name="connectionString">The connection string to parse</param>
    /// <returns>A configured KurrentDBClientSettings instance</returns>
    public static KurrentDBClientSettings Create(string connectionString) =>
        KurrentConnectionString.Parse(connectionString).ToKurrentDBClientSettings();
}

static class KurrentDBConnectionStringConverter {
    static readonly Dictionary<string, Type> SettingsType =
        new(StringComparer.InvariantCultureIgnoreCase) {
            { KurrentConnectionString.ConnectionName, typeof(string) },
            { KurrentConnectionString.MaxDiscoverAttempts, typeof(int) },
            { KurrentConnectionString.DiscoveryInterval, typeof(int) },
            { KurrentConnectionString.GossipTimeout, typeof(int) },
            { KurrentConnectionString.NodePreference, typeof(string) },
            { KurrentConnectionString.Tls, typeof(bool) },
            { KurrentConnectionString.TlsVerifyCert, typeof(bool) },
            { KurrentConnectionString.TlsCaFile, typeof(string) },
            { KurrentConnectionString.DefaultDeadline, typeof(int) },
            { KurrentConnectionString.ThrowOnAppendFailure, typeof(bool) },
            { KurrentConnectionString.KeepAliveInterval, typeof(int) },
            { KurrentConnectionString.KeepAliveTimeout, typeof(int) },
            { KurrentConnectionString.UserCertFile, typeof(string) },
            { KurrentConnectionString.UserKeyFile, typeof(string) }
        };

    public static KurrentDBClientSettings ToKurrentDBClientSettings(this KurrentConnectionString connectionString) {
        var settings = new KurrentDBClientSettings {
            ConnectivitySettings = KurrentDBClientConnectivitySettings.Default,
            OperationOptions     = KurrentDBClientOperationOptions.Default
        };

        if (connectionString.UserInfo.HasValue)
            settings.DefaultCredentials = new(connectionString.UserInfo.Value.User, connectionString.UserInfo.Value.Password);

        var typedOptions = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
        foreach (var kv in connectionString.Options) {
            if (!SettingsType.TryGetValue(kv.Key, out var type))
                throw new InvalidLegacyClientSettingsException($"Unknown option: {kv.Key}");

            if (type == typeof(int)) {
                if (!int.TryParse(kv.Value, out var intValue))
                    throw new InvalidLegacyClientSettingsException($"{kv.Key} must be an integer value");

                typedOptions.Add(kv.Key, intValue);
            }
            else if (type == typeof(bool)) {
                if (!bool.TryParse(kv.Value, out var boolValue))
                    throw new InvalidLegacyClientSettingsException($"{kv.Key} must be either true or false");

                typedOptions.Add(kv.Key, boolValue);
            }
            else if (type == typeof(string)) {
                typedOptions.Add(kv.Key, kv.Value);
            }
        }

        if (typedOptions.TryGetValue(KurrentConnectionString.ConnectionName, out var connectionName))
            settings.ConnectionName = (string)connectionName;

        if (typedOptions.TryGetValue(KurrentConnectionString.MaxDiscoverAttempts, out var maxDiscoverAttempts))
            settings.ConnectivitySettings.MaxDiscoverAttempts = (int)maxDiscoverAttempts;

        if (typedOptions.TryGetValue(KurrentConnectionString.DiscoveryInterval, out var discoveryInterval))
            settings.ConnectivitySettings.DiscoveryInterval = TimeSpan.FromMilliseconds((int)discoveryInterval);

        if (typedOptions.TryGetValue(KurrentConnectionString.GossipTimeout, out var gossipTimeout))
            settings.ConnectivitySettings.GossipTimeout = TimeSpan.FromMilliseconds((int)gossipTimeout);

        if (typedOptions.TryGetValue(KurrentConnectionString.NodePreference, out var nodePreference))
            settings.ConnectivitySettings.NodePreference = ((string)nodePreference).ToLowerInvariant() switch {
                "leader"          => NodePreference.Leader,
                "follower"        => NodePreference.Follower,
                "random"          => NodePreference.Random,
                "readonlyreplica" => NodePreference.ReadOnlyReplica,
                _                 => throw new InvalidLegacyClientSettingsException($"Invalid NodePreference: {nodePreference}")
            };

        var useTls = true;
        if (typedOptions.TryGetValue(KurrentConnectionString.Tls, out var tls))
            useTls = (bool)tls;

        if (typedOptions.TryGetValue(KurrentConnectionString.DefaultDeadline, out var operationTimeout))
            settings.DefaultDeadline = TimeSpan.FromMilliseconds((int)operationTimeout);

        if (typedOptions.TryGetValue(KurrentConnectionString.ThrowOnAppendFailure, out var throwOnAppendFailure))
            settings.OperationOptions.ThrowOnAppendFailure = (bool)throwOnAppendFailure;

        if (typedOptions.TryGetValue(KurrentConnectionString.KeepAliveInterval, out var keepAliveIntervalMs))
            settings.ConnectivitySettings.KeepAliveInterval = keepAliveIntervalMs switch {
                -1                 => Timeout.InfiniteTimeSpan,
                int value and >= 0 => TimeSpan.FromMilliseconds(value),
                _                  => throw new InvalidLegacyClientSettingsException($"Invalid KeepAliveInterval: {keepAliveIntervalMs}")
            };

        if (typedOptions.TryGetValue(KurrentConnectionString.KeepAliveTimeout, out var keepAliveTimeoutMs))
            settings.ConnectivitySettings.KeepAliveTimeout = keepAliveTimeoutMs switch {
                -1                 => Timeout.InfiniteTimeSpan,
                int value and >= 0 => TimeSpan.FromMilliseconds(value),
                _                  => throw new InvalidLegacyClientSettingsException($"Invalid KeepAliveTimeout: {keepAliveTimeoutMs}")
            };

        settings.ConnectivitySettings.Insecure = !useTls;

        if (connectionString.Hosts.Length == 1 && !KurrentConnectionString.DiscoverySchemes.Contains(connectionString.Scheme)) {
            settings.ConnectivitySettings.Address = connectionString.Hosts[0].ToUri(useTls);
        }
        else {
            settings.ConnectivitySettings.Address     = null; //new Uri("kurrentdb+discover://cluster");
            settings.ConnectivitySettings.GossipSeeds = connectionString.Hosts;
        }

        if (typedOptions.TryGetValue(KurrentConnectionString.TlsVerifyCert, out var tlsVerifyCert))
            settings.ConnectivitySettings.TlsVerifyCert = (bool)tlsVerifyCert;

        if (typedOptions.TryGetValue(KurrentConnectionString.TlsCaFile, out var tlsCaFile)) {
            var tlsCaFilePath = Path.GetFullPath((string)tlsCaFile);
            if (!string.IsNullOrEmpty(tlsCaFilePath) && !File.Exists(tlsCaFilePath))
                throw new InvalidLegacyClientSettingsException("Failed to load certificate. File was not found.");

            try {
                settings.ConnectivitySettings.TlsCaFile = X509CertificateLoader.LoadCertificateFromFile(tlsCaFilePath);
            }
            catch (CryptographicException) {
                throw new InvalidLegacyClientSettingsException("Failed to load certificate. Invalid file format.");
            }
        }

        ConfigureClientCertificate(settings, typedOptions);

        settings.CreateHttpMessageHandler = CreateDefaultHandler;

        return settings;

        HttpMessageHandler CreateDefaultHandler() {
            var handler = new SocketsHttpHandler {
                KeepAlivePingDelay             = settings.ConnectivitySettings.KeepAliveInterval,
                KeepAlivePingTimeout           = settings.ConnectivitySettings.KeepAliveTimeout,
                EnableMultipleHttp2Connections = true
            };

            if (settings.ConnectivitySettings.Insecure)
                return handler;

            if (settings.ConnectivitySettings.ClientCertificate is not null)
                handler.SslOptions.ClientCertificates = new() {
                    settings.ConnectivitySettings.ClientCertificate
                };

            handler.SslOptions.RemoteCertificateValidationCallback = settings.ConnectivitySettings.TlsVerifyCert switch {
                false => delegate { return true; },
                true when settings.ConnectivitySettings.TlsCaFile is not null => (_, certificate, chain, _) => {
                    if (certificate is not X509Certificate2 peerCertificate || chain is null) return false;

                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(settings.ConnectivitySettings.TlsCaFile);
                    return chain.Build(peerCertificate);
                },
                _ => null
            };

            return handler;
        }

        static void ConfigureClientCertificate(KurrentDBClientSettings settings, IReadOnlyDictionary<string, object> options) {
            var certPemFilePath = GetOptionValueAsString(KurrentConnectionString.UserCertFile);
            var keyPemFilePath  = GetOptionValueAsString(KurrentConnectionString.UserKeyFile);

            if (string.IsNullOrEmpty(certPemFilePath) && string.IsNullOrEmpty(keyPemFilePath))
                return;

            if (string.IsNullOrEmpty(certPemFilePath) || string.IsNullOrEmpty(keyPemFilePath))
                throw new InvalidLegacyClientSettingsException("Invalid client certificate settings. Both UserCertFile and UserKeyFile must be set.");

            if (!File.Exists(certPemFilePath))
                throw new InvalidLegacyClientSettingsException($"Invalid client certificate settings. The specified UserCertFile does not exist: {certPemFilePath}");

            if (!File.Exists(keyPemFilePath))
                throw new InvalidLegacyClientSettingsException($"Invalid client certificate settings. The specified UserKeyFile does not exist: {keyPemFilePath}");

            try {
                settings.ConnectivitySettings.ClientCertificate = X509Certificates.CreateFromPemFile(certPemFilePath, keyPemFilePath);
            }
            catch (Exception ex) {
                throw new InvalidLegacyClientSettingsException($"Failed to create client certificate: {ex.Message}", ex);
            }

            return;

            string GetOptionValueAsString(string key) => options.TryGetValue(key, out var value) ? (string)value : "";
        }
    }
}

public class InvalidLegacyClientSettingsException(string reason, Exception? innerException = null)
    : KurrentException(ErrorCode.FromType<InvalidLegacyClientSettingsException>(), reason, innerException);
