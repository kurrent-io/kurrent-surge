using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Kurrent.Surge.Client;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// A load-balanced gRPC channel over a KurrentDB cluster. Discovery, node preference,
/// leader redirects and failover run inside the channel through <see cref="GossipResolver"/>
/// and <see cref="NodePreferenceBalancer"/>, so callers use a plain <see cref="Grpc.Core.CallInvoker"/>
/// instead of the legacy deferred call-invoker bridge.
/// </summary>
sealed class ClusterChannel : IAsyncDisposable {
    const int MaxReceiveMessageLength = 17 * 1024 * 1024; // 17MB

    readonly GossipResolverFactory _resolverFactory;
    readonly IGossipClient         _gossipClient;
    readonly SocketsHttpHandler    _channelHandler;
    readonly SocketsHttpHandler    _gossipHandler;
    readonly ServiceProvider       _services;

    bool _disposed;

    ClusterChannel(
        GrpcChannel channel, CallInvoker callInvoker, GossipResolverFactory resolverFactory,
        IGossipClient gossipClient, SocketsHttpHandler channelHandler, SocketsHttpHandler gossipHandler,
        ServiceProvider services
    ) {
        Channel          = channel;
        CallInvoker      = callInvoker;
        _resolverFactory = resolverFactory;
        _gossipClient    = gossipClient;
        _channelHandler  = channelHandler;
        _gossipHandler   = gossipHandler;
        _services        = services;
    }

    public GrpcChannel Channel     { get; }
    public CallInvoker CallInvoker { get; }

    public string Target => Channel.Target;

    public static ClusterChannel Create(KurrentClientOptions options) {
        var transport = ResolveTransportSecurity(options);
        var scheme    = transport.Insecure ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;

        // The balanced channel's handler cannot be shared with the gossip channels:
        // the balancer installs its own ConnectCallback on the handler it owns, which
        // would hijack plain direct-channel requests going through the same instance.
        var channelHandler = CreateHandler(options, transport);
        var gossipHandler  = CreateHandler(options, transport);

        var useGossip = options.ConnectionScheme == KurrentConnectionScheme.Discover;

        var gossipClient = new GrpcGossipClient(
            endpoint => GrpcChannel.ForAddress(
                new UriBuilder(scheme, endpoint.Host, endpoint.Port).Uri,
                new() {
                    HttpHandler                          = gossipHandler,
                    DisposeHttpClient                    = false,
                    LoggerFactory                        = options.LoggerFactory,
                    ThrowOperationCanceledOnCancellation = true
                }),
            options.Gossip.Timeout
        );

        var resolverFactory = new GossipResolverFactory(
            new() {
                Seeds               = useGossip ? options.Endpoints : [options.Endpoints[0]],
                NodePreference      = options.Gossip.ReadPreference,
                MaxDiscoverAttempts = options.Gossip.MaxDiscoverAttempts,
                DiscoveryInterval   = options.Gossip.DiscoveryInterval,
                UseGossip           = useGossip
            },
            gossipClient
        );

        var services = new ServiceCollection()
            .AddSingleton<ResolverFactory>(resolverFactory)
            .AddSingleton<LoadBalancerFactory>(new NodePreferenceBalancerFactory())
            .BuildServiceProvider();

        var serviceConfig = new ServiceConfig {
            LoadBalancingConfigs = { new LoadBalancingConfig(NodePreferenceBalancerFactory.PolicyName) }
        };

        if (options.Resilience.Retry.Enabled)
            serviceConfig.MethodConfigs.Add(CreateRetryMethodConfig(options));

        var channel = GrpcChannel.ForAddress(
            $"{GossipResolverFactory.Scheme}:///{options.ConnectionName}",
            new() {
                Credentials                          = transport.Insecure ? ChannelCredentials.Insecure : ChannelCredentials.SecureSsl,
                HttpHandler                          = channelHandler,
                DisposeHttpClient                    = false,
                LoggerFactory                        = options.LoggerFactory,
                ServiceProvider                      = services,
                ServiceConfig                        = serviceConfig,
                MaxReceiveMessageSize                = MaxReceiveMessageLength,
                ThrowOperationCanceledOnCancellation = true
            }
        );

        var clientName    = AppVersionInfo.Current.ProductName ?? "KurrentDB .NET Client";
        var clientVersion = AppVersionInfo.Current.ProductVersion ?? AppVersionInfo.Current.FileVersion ?? "0.0.0";

        var headersInterceptor = new ClientHeadersInterceptor(new Dictionary<string, string> {
            ["kurrentdb.client.name"]    = clientName,
            ["kurrentdb.client.version"] = clientVersion,
            ["connection-name"]          = options.ConnectionName
        });

        var leaderChangeInterceptor = new LeaderChangeInterceptor(
            leader => resolverFactory.Resolver?.AnnounceLeader(leader),
            options.LoggerFactory.CreateLogger<LeaderChangeInterceptor>()
        );

        var callInvoker = channel
            .CreateCallInvoker()
            .Intercept([..options.Interceptors, headersInterceptor, leaderChangeInterceptor]);

        return new(channel, callInvoker, resolverFactory, gossipClient, channelHandler, gossipHandler, services);
    }

    /// <summary>
    /// Pre-warms the channel by running discovery and connecting ahead of the first call.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Channel.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Forces a topology refresh. With a leader endpoint, routes straight to that node;
    /// without one, re-runs gossip discovery.
    /// </summary>
    public void TriggerReconnect(DnsEndPoint? leaderEndpoint = null) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var resolver = _resolverFactory.Resolver;
        if (resolver is null)
            return;

        if (leaderEndpoint is not null)
            resolver.AnnounceLeader(leaderEndpoint);
        else
            resolver.Refresh();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;

        _disposed = true;

        Channel.Dispose();
        await _gossipClient.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
        _channelHandler.Dispose();
        _gossipHandler.Dispose();
    }

    #region ->> Options Mapping <<-

    sealed record TransportSecurity(bool Insecure, bool VerifyServerCertificate, X509Certificate2? TrustedRoot);

    static TransportSecurity ResolveTransportSecurity(KurrentClientOptions options) {
        var insecure    = false;
        var verifyCert  = true;
        var trustedRoot = (X509Certificate2?)null;

        options.Security.Transport.Switch(
            noTransportSecurity => insecure = true,
            standardTls         => verifyCert = standardTls.VerifyServerCertificate,
            fileCertificate     => {
                verifyCert = fileCertificate.VerifyServerCertificate;

                var caPath = Path.GetFullPath(options.Security.Transport.AsFileCertificateTls.CaPath);
                if (!string.IsNullOrEmpty(caPath) && !File.Exists(caPath))
                    throw new CryptographicException("Failed to load certificate. File was not found.");

                try {
                    trustedRoot = X509CertificateLoader.LoadCertificateFromFile(caPath);
                }
                catch (CryptographicException) {
                    throw new CryptographicException("Failed to load certificate. Invalid file format.");
                }
            },
            x509Certificate => {
                verifyCert  = x509Certificate.VerifyServerCertificate;
                trustedRoot = x509Certificate.Certificate;
            }
        );

        return new(insecure, verifyCert, trustedRoot);
    }

    static SocketsHttpHandler CreateHandler(KurrentClientOptions options, TransportSecurity transport) {
        var handler = new SocketsHttpHandler {
            KeepAlivePingDelay             = options.Resilience.KeepAliveInterval,
            KeepAlivePingTimeout           = options.Resilience.KeepAliveTimeout,
            EnableMultipleHttp2Connections = true
        };

        if (transport.Insecure)
            return handler;

        if (options.Security.Authentication.Value is X509CertificateCredentials clientCertificate)
            handler.SslOptions.ClientCertificates = new() { clientCertificate.Certificate };

        handler.SslOptions.RemoteCertificateValidationCallback = transport.VerifyServerCertificate switch {
            false => delegate { return true; },
            true when transport.TrustedRoot is not null => (_, certificate, chain, _) => {
                if (certificate is not X509Certificate2 peerCertificate || chain is null)
                    return false;

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(transport.TrustedRoot);
                return chain.Build(peerCertificate);
            },
            _ => null
        };

        return handler;
    }

    static MethodConfig CreateRetryMethodConfig(KurrentClientOptions options) {
        var retry = options.Resilience.Retry;

        var retryPolicy = new RetryPolicy {
            MaxAttempts       = retry.MaxAttempts,
            InitialBackoff    = retry.InitialBackoff,
            MaxBackoff        = retry.MaxBackoff,
            BackoffMultiplier = retry.BackoffMultiplier
        };

        // The declarative channel retry policy cannot consult a predicate: these are the
        // classic transient codes the legacy policy always retried.
        retryPolicy.RetryableStatusCodes.Add(StatusCode.Unavailable);
        retryPolicy.RetryableStatusCodes.Add(StatusCode.Unknown);
        retryPolicy.RetryableStatusCodes.Add(StatusCode.DeadlineExceeded);
        retryPolicy.RetryableStatusCodes.Add(StatusCode.ResourceExhausted);

        return new() {
            Names       = { MethodName.Default },
            RetryPolicy = retryPolicy
        };
    }

    #endregion
}
