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
    readonly string                        _fallbackTarget;

    bool _disposed;

    ClusterChannel(
        GrpcChannel channel, CallInvoker callInvoker, GossipResolverFactory resolverFactory,
        NodePreferenceBalancerFactory balancerFactory, IGossipClient gossipClient,
        SocketsHttpHandler channelHandler, SocketsHttpHandler gossipHandler,
        ServiceProvider services, string fallbackTarget
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

    public string Target {
        get {
            var endpoint = _balancerFactory.Balancer?.ConnectedEndpoint;
            return endpoint is not null ? $"{endpoint.Host}:{endpoint.Port}" : _fallbackTarget;
        }
    }

    public static ClusterChannel Create(KurrentClientOptions options, NodePreference? nodePreference = null) {
        var insecure = options.Security.Transport.IsInsecure;
        var scheme   = insecure ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;

        // The balanced channel's handler cannot be shared with the gossip channels:
        // the balancer installs its own ConnectCallback on the handler it owns, which
        // would hijack plain direct-channel requests going through the same instance.
        var channelHandler = CreateHandler(options);
        var gossipHandler  = CreateHandler(options);

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
                NodePreference      = nodePreference ?? options.Gossip.ReadPreference,
                MaxDiscoverAttempts = options.Gossip.MaxDiscoverAttempts,
                DiscoveryInterval   = options.Gossip.DiscoveryInterval,
                UseGossip           = useGossip
            },
            gossipClient
        );

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
            var authorizationHeader = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}"))}";

            callCredentials = CallCredentials.FromInterceptor((_, metadata) => {
                metadata.Add("authorization", authorizationHeader);
                return Task.CompletedTask;
            });
        }

        var transportCredentials = insecure ? ChannelCredentials.Insecure : ChannelCredentials.SecureSsl;
        var channelCredentials   = callCredentials is null ? transportCredentials : ChannelCredentials.Create(transportCredentials, callCredentials);

        var channel = GrpcChannel.ForAddress(
            $"{GossipResolverFactory.Scheme}:///{options.ConnectionName}",
            new() {
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

        var fallbackEndpoint = options.Endpoints[0];
        var fallbackTarget   = $"{fallbackEndpoint.Host}:{fallbackEndpoint.Port}";

        return new(
            channel, callInvoker, resolverFactory, balancerFactory, gossipClient,
            channelHandler, gossipHandler, services, fallbackTarget
        );

        static SocketsHttpHandler CreateHandler(KurrentClientOptions options) {
            var handler = new SocketsHttpHandler {
                KeepAlivePingDelay             = options.Resilience.KeepAliveInterval,
                KeepAlivePingTimeout           = options.Resilience.KeepAliveTimeout,
                EnableMultipleHttp2Connections = true
            };

            if (options.Security.Transport.IsInsecure)
                return handler;

            if (options.Security.Authentication.TryGetCertificate(out var clientCertificate))
                handler.SslOptions.ClientCertificates = new() { clientCertificate };

            handler.SslOptions.RemoteCertificateValidationCallback = options.Security.Transport.VerifyServerCertificate switch {
                false => delegate { return true; },
                true when options.Security.Transport.TryGetCertificate(out var trustedRoot) =>
                    new RemoteCertificateValidator(trustedRoot, options.LoggerFactory.CreateLogger<RemoteCertificateValidator>()),
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

}
