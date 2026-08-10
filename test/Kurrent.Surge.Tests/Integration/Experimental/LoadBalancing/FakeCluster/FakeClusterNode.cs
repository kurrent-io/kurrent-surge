using System.Net;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Google.Rpc;
using Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster.Protocol;
using Any = Google.Protobuf.WellKnownTypes.Any;
using Metadata = Grpc.Core.Metadata;
using RpcStatus = Google.Rpc.Status;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing.FakeCluster;

/// <summary>
/// One in-process cluster node: a Kestrel HTTP/2 endpoint on a dynamic loopback port
/// serving the gossip service over the shared cluster view.
/// </summary>
public sealed class FakeClusterNode(FakeClusterHarness cluster, MemberInfo.Types.VNodeState state, FakeClusterTls? tls = null) : IAsyncDisposable {
    WebApplication? _app;
    long            _servedCalls;

    public FakeClusterHarness Cluster    { get; } = cluster;
    public Guid               InstanceId { get; } = Guid.NewGuid();

    public MemberInfo.Types.VNodeState State { get; set; } = state;

    public bool IsAlive { get; set; } = true;

    public DnsEndPoint Endpoint { get; private set; } = null!;

    /// <summary>
    /// When set, every call served by this node fails with the V2 not-leader error
    /// advertising this endpoint as the current leader.
    /// </summary>
    public DnsEndPoint? NotLeaderRedirect { get; set; }

    /// <summary>Number of calls this node served successfully.</summary>
    public long ServedCalls => Interlocked.Read(ref _servedCalls);

    /// <summary>Request headers of the last call served by this node.</summary>
    public Metadata? LastRequestHeaders { get; set; }

    /// <summary>Client certificate presented on the last call served by this node, when TLS is enabled.</summary>
    public X509Certificate2? LastClientCertificate { get; set; }

    internal void RecordServedCall() => Interlocked.Increment(ref _servedCalls);

    public async ValueTask StartAsync() {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(this);
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => {
                listen.Protocols = HttpProtocols.Http2;

                if (tls is not null) {
                    listen.UseHttps(tls.ServerCertificate, https => {
                        https.ClientCertificateMode      = ClientCertificateMode.RequireCertificate;
                        https.ClientCertificateValidation = (_, _, _) => true;
                    });
                }
            }));

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
        node.LastRequestHeaders    = context.RequestHeaders;
        node.LastClientCertificate = context.GetHttpContext().Connection.ClientCertificate;

        // Mirrors the server's ApiErrors.NotLeaderNode: FailedPrecondition with a
        // google.rpc.Status in grpc-status-details-bin carrying ErrorInfo plus the
        // Any-packed NotLeaderNodeErrorDetails.
        if (node.NotLeaderRedirect is { } leader) {
            throw new RpcStatus {
                Code    = (int)Code.FailedPrecondition,
                Message = "The server is not the leader node and cannot handle the request.",
                Details = {
                    Any.Pack(new Google.Rpc.ErrorInfo {
                        Reason = "SERVER_ERROR_NOT_LEADER_NODE",
                        Domain = "kurrentdb"
                    }),
                    Any.Pack(new Kurrent.Rpc.NotLeaderNodeErrorDetails {
                        CurrentLeader = new() {
                            Host = leader.Host,
                            Port = leader.Port
                        }
                    })
                }
            }.ToRpcException();
        }

        node.RecordServedCall();
        context.ResponseTrailers.Add("serving-node-port", node.Endpoint.Port.ToString());

        return Task.FromResult(node.Cluster.BuildClusterInfo());
    }
}
