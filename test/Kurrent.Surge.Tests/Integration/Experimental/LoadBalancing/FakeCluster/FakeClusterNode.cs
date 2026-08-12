using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster.Protocol;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster;

/// <summary>
/// One in-process cluster node: a Kestrel HTTP/2 endpoint on a dynamic loopback port
/// serving the gossip service over the shared cluster view.
/// </summary>
public sealed class FakeClusterNode(FakeClusterHarness cluster, MemberInfo.Types.VNodeState state) : IAsyncDisposable {
    WebApplication? _app;
    long            _servedCalls;

    public FakeClusterHarness Cluster    { get; } = cluster;
    public Guid               InstanceId { get; } = Guid.NewGuid();

    public MemberInfo.Types.VNodeState State { get; set; } = state;

    public bool IsAlive { get; set; } = true;

    public DnsEndPoint Endpoint { get; private set; } = null!;

    /// <summary>
    /// When set, every call served by this node fails with the legacy not-leader
    /// trailers advertising this endpoint as the current leader.
    /// </summary>
    public DnsEndPoint? NotLeaderRedirect { get; set; }

    /// <summary>Number of calls this node served successfully.</summary>
    public long ServedCalls => Interlocked.Read(ref _servedCalls);

    /// <summary>Request headers of the last call served by this node.</summary>
    public Metadata? LastRequestHeaders { get; set; }

    internal void RecordServedCall() => Interlocked.Increment(ref _servedCalls);

    public async ValueTask StartAsync() {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(this);
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));

        _app = builder.Build();
        _app.MapGrpcService<FakeGossipService>();

        await _app.StartAsync();

        var address = _app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        Endpoint = new("127.0.0.1", new Uri(address).Port);
    }

    public async ValueTask StopAsync() {
        if (_app is null)
            return;

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public ValueTask DisposeAsync() => StopAsync();
}

/// <summary>
/// Serves the shared cluster view and doubles as the probe RPC target for routing
/// assertions: successful responses carry a <c>serving-node-port</c> trailer.
/// </summary>
sealed class FakeGossipService(FakeClusterNode node) : Gossip.GossipBase {
    public override Task<ClusterInfo> Read(Empty request, ServerCallContext context) {
        node.LastRequestHeaders = context.RequestHeaders;

        if (node.NotLeaderRedirect is { } leader) {
            throw new RpcException(
                new(StatusCode.FailedPrecondition, "Leader info available"),
                new Metadata {
                    { "exception", "not-leader" },
                    { "leader-endpoint-host", leader.Host },
                    { "leader-endpoint-port", leader.Port.ToString() }
                }
            );
        }

        node.RecordServedCall();
        context.ResponseTrailers.Add("serving-node-port", node.Endpoint.Port.ToString());

        return Task.FromResult(node.Cluster.BuildClusterInfo());
    }
}
