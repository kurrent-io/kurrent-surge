using System.Net;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Kurrent.Http;
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

    readonly GossipResolverFactory         _resolverFactory;
    readonly NodePreferenceBalancerFactory _balancerFactory;
    readonly IGossipClient                 _gossipClient;
    readonly SocketsHttpHandler            _channelHandler;
    readonly SocketsHttpHandler            _gossipHandler;
    readonly ServiceProvider               _services;
    readonly ClusterEndpoint               _fallbackTarget;

    bool _disposed;

    ClusterChannel(
        GrpcChannel channel, CallInvoker callInvoker, GossipResolverFactory resolverFactory,
        NodePreferenceBalancerFactory balancerFactory, IGossipClient gossipClient,
        SocketsHttpHandler channelHandler, SocketsHttpHandler gossipHandler,
        ServiceProvider services, ClusterEndpoint fallbackTarget
    ) {
        Channel          = channel;
        CallInvoker      = callInvoker;
        _resolverFactory = resolverFactory;
        _balancerFactory = balancerFactory;
        _gossipClient    = gossipClient;
        _channelHandler  = channelHandler;
        _gossipHandler   = gossipHandler;
        _services        = services;
        _fallbackTarget  = fallbackTarget;
    }

    public GrpcChannel Channel     { get; }
    public CallInvoker CallInvoker { get; }

    public string Target => (_balancerFactory.Balancer?.ConnectedEndpoint ?? _fallbackTarget).ToString();

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
    public void TriggerReconnect(ClusterEndpoint? leaderEndpoint = null) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var resolver = _resolverFactory.Resolver;
        if (resolver is null)
            return;

        if (leaderEndpoint is { } leader)
            resolver.AnnounceLeader(leader);
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
    
    public static ClusterChannel Create(KurrentClientOptions options, NodeReadPreference? nodePreference = null) {
        var insecure = options.Security.Transport.IsInsecure;
        var scheme   = insecure ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;

        // The balanced channel's handler cannot be shared with the gossip channels:
        // the balancer installs its own ConnectCallback on the handler it owns, which
        // would hijack plain direct-channel requests going through the same instance.
        var channelHandler = CreateHandler(options);
        var gossipHandler  = CreateHandler(options);
        
        var gossipClient    = CreateGossipClient(options, gossipHandler, scheme);
        var resolverFactory = CreateResolverFactory(options, nodePreference, gossipClient);

        var balancerFactory = new NodePreferenceBalancerFactory();

        var services = new ServiceCollection()
            .AddSingleton<ResolverFactory>(resolverFactory)
            .AddSingleton<LoadBalancerFactory>(balancerFactory)
            .BuildServiceProvider();

        var serviceConfig = new ServiceConfig {
            LoadBalancingConfigs = { new LoadBalancingConfig(NodePreferenceBalancerFactory.PolicyName) }
        };

        if (options.Resilience.Retry.Enabled)
            serviceConfig.MethodConfigs.Add(CreateRetryMethodConfig(options));

        CallCredentials? callCredentials = null;

        if (options.Security.Authentication.Value is BasicCredentials basic) {
            var authorizationHeader = $"Basic {Convert.ToBase64String(inArray: Encoding.UTF8.GetBytes(s: $"{basic.Username}:{basic.Password}"))}";

            callCredentials = CallCredentials.FromInterceptor(interceptor: (_, metadata) => {
                metadata.Add(key: "authorization", value: authorizationHeader);
                return Task.CompletedTask;
            });
        }

        var transportCredentials = insecure ? ChannelCredentials.Insecure : ChannelCredentials.SecureSsl;
        var channelCredentials   = callCredentials is null ? transportCredentials : ChannelCredentials.Create(channelCredentials: transportCredentials, callCredentials: callCredentials);

        var channel = GrpcChannel.ForAddress(
            address: $"{GossipResolverFactory.Scheme}:///{options.ConnectionName}",
            channelOptions: new() {
                Credentials                             = channelCredentials,
                HttpHandler                             = channelHandler,
                DisposeHttpClient                       = false,
                LoggerFactory                           = options.LoggerFactory,
                ServiceProvider                         = services,
                ServiceConfig                           = serviceConfig,
                MaxReceiveMessageSize                   = MaxReceiveMessageLength,
                ThrowOperationCanceledOnCancellation    = true,
                UnsafeUseInsecureChannelCallCredentials = insecure && callCredentials is not null
            }
        );

        var clientName    = AppVersionInfo.Current.ProductName ?? "KurrentDB .NET Client";
        var clientVersion = AppVersionInfo.Current.ProductVersion ?? AppVersionInfo.Current.FileVersion ?? "0.0.0";

        var headersInterceptor = new ClientHeadersInterceptor(staticHeaders: new Dictionary<string, string> {
            [key: "kurrentdb.client.name"]    = clientName,
            [key: "kurrentdb.client.version"] = clientVersion,
            [key: "connection-name"]          = options.ConnectionName
        });

        var leaderChangeInterceptor = new LeaderChangeInterceptor(
            announceLeader: leader => resolverFactory.Resolver?.AnnounceLeader(leader: leader),
            logger: options.LoggerFactory.CreateLogger<LeaderChangeInterceptor>()
        );

        var callInvoker = channel
            .CreateCallInvoker()
            .Intercept(interceptors: [..options.Interceptors, headersInterceptor, leaderChangeInterceptor]);

        return new(
            channel: channel, 
            callInvoker: callInvoker, 
            resolverFactory: resolverFactory, 
            balancerFactory: balancerFactory, 
            gossipClient: gossipClient,
            channelHandler: channelHandler, 
            gossipHandler: gossipHandler, 
            services: services,
            fallbackTarget: ClusterEndpoint.From(options.Endpoints[0])
        );

        static GrpcGossipClient CreateGossipClient(KurrentClientOptions options, SocketsHttpHandler gossipHandler, string scheme) =>
            new(
                createChannel: endpoint => GrpcChannel.ForAddress(
                    address: new UriBuilder(scheme: scheme, host: endpoint.Host, portNumber: endpoint.Port).Uri,
                    channelOptions: new() {
                        HttpHandler                          = gossipHandler,
                        DisposeHttpClient                    = false,
                        LoggerFactory                        = options.LoggerFactory,
                        ThrowOperationCanceledOnCancellation = true
                    }),
                timeout: options.Gossip.Timeout
            );

        static GossipResolverFactory CreateResolverFactory(KurrentClientOptions options, NodeReadPreference? nodePreference, IGossipClient gossipClient) {
            var seeds            = options.Endpoints.Select(selector: ClusterEndpoint.From).ToArray();
            var discoveryEnabled = options.ConnectionScheme == KurrentConnectionScheme.Discover;

            return new(
                options: new() {
                    Seeds               = discoveryEnabled ? seeds : [seeds[0]],
                    NodeReadPreference  = nodePreference ?? options.Gossip.ReadPreference,
                    MaxDiscoverAttempts = options.Gossip.MaxDiscoverAttempts,
                    DiscoveryInterval   = options.Gossip.DiscoveryInterval,
                    DiscoveryEnabled    = discoveryEnabled
                },
                gossip: gossipClient
            );
        }

        static SocketsHttpHandler CreateHandler(KurrentClientOptions options) {
            var handler = new SocketsHttpHandler {
                KeepAlivePingDelay             = options.Resilience.KeepAliveInterval,
                KeepAlivePingTimeout           = options.Resilience.KeepAliveTimeout,
                EnableMultipleHttp2Connections = true
            };

            if (options.Security.Transport.IsInsecure)
                return handler;

            if (options.Security.Authentication.TryGetCertificate(certificate: out var clientCertificate))
                handler.SslOptions.ClientCertificates = new() { clientCertificate };

            handler.SslOptions.RemoteCertificateValidationCallback = options.Security.Transport.VerifyServerCertificate switch {
                false => delegate { return true; },
                true when options.Security.Transport.TryGetCertificate(certificate: out var trustedRoot) =>
                    new RemoteCertificateValidator(CaCertificate: trustedRoot, Logger: options.LoggerFactory.CreateLogger<RemoteCertificateValidator>()),
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
            retryPolicy.RetryableStatusCodes.Add(item: StatusCode.Unavailable);
            retryPolicy.RetryableStatusCodes.Add(item: StatusCode.Unknown);
            retryPolicy.RetryableStatusCodes.Add(item: StatusCode.DeadlineExceeded);
            retryPolicy.RetryableStatusCodes.Add(item: StatusCode.ResourceExhausted);

            return new() {
                Names       = { MethodName.Default },
                RetryPolicy = retryPolicy
            };
        }
    }
}
