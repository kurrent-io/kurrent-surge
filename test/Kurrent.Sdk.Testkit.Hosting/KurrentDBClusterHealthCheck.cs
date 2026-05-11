using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Refit;

namespace Kurrent.Sdk.Testkit.Hosting;

public sealed partial class KurrentDBClusterHealthCheck(
    KurrentDBServerResource server,
    IHttpClientFactory httpClientFactory,
    ResourceConnectionProvider provider,
    ILoggerFactory loggerFactory
) : IHealthCheck {
    internal const string HttpClientName = "KurrentDBGossip";

    static readonly RefitSettings RefitSettings = new() {
        ContentSerializer = new SystemTextJsonContentSerializer(
            new() { TypeInfoResolver = GossipJsonContext.Default })
    };

    static readonly HashSet<string> AliveStates = new(StringComparer.OrdinalIgnoreCase) {
        "Leader", "Follower", "ReadOnlyReplica"
    };

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) {
        var logger = loggerFactory.CreateLogger<KurrentDBClusterHealthCheck>();

        try {
            var node = server.Nodes[0];

            LogCheckingClusterHealth(logger, server.Name, node.Name);

            var connectionString = await provider
                .GetConnectionStringAsync(node.Name, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            var connUri = new Uri(connectionString.Replace("kurrentdb://", "https://").Replace("kurrentdb+discover://", "https://"));
            var scheme = server.TlsEnabled ? "https" : "http";
            var baseUrl = new Uri($"{scheme}://{connUri.Host}:{connUri.Port}");

            LogGossipEndpoint(logger, baseUrl.ToString());

            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            httpClient.BaseAddress = baseUrl;

            var api = RestService.For<IKurrentDBGossipApi>(httpClient, RefitSettings);
            var gossip = await api.GetGossipAsync(ct).ConfigureAwait(false);

            var alive = gossip.Members.Count(m => m.IsAlive && AliveStates.Contains(m.State));
            var total = gossip.Members.Count;
            var hasLeader = gossip.Members.Any(m => m is { IsAlive: true, State: "Leader" });

            var data = new Dictionary<string, object> {
                ["alive"] = alive,
                ["total"] = total,
                ["hasLeader"] = hasLeader,
                ["members"] = gossip.Members.Select(m => $"{m.State}(alive={m.IsAlive})").ToList()
            };

            if (!hasLeader) {
                LogNoLeader(logger, server.Name);
                return HealthCheckResult.Unhealthy("No leader node found", data: data);
            }

            var quorum = total / 2 + 1;

            if (alive < quorum) {
                LogDegraded(logger, server.Name, alive, total);
                return HealthCheckResult.Degraded($"{alive}/{total} nodes alive, below quorum ({quorum})", data: data);
            }

            LogHealthy(logger, server.Name, alive, total);
            return HealthCheckResult.Healthy($"{alive}/{total} nodes alive, leader present", data: data);
        }
        catch (Exception ex) {
            return new(context.Registration.FailureStatus, exception: ex);
        }
    }

    #region ->> Logging <<-

    [LoggerMessage(LogLevel.Debug, "Checking cluster health for {ServerName} via node {NodeName}")]
    static partial void LogCheckingClusterHealth(ILogger logger, string serverName, string nodeName);

    [LoggerMessage(LogLevel.Debug, "Gossip endpoint: {BaseUrl}/gossip")]
    static partial void LogGossipEndpoint(ILogger logger, string baseUrl);

    [LoggerMessage(LogLevel.Warning, "{ServerName} cluster has no leader")]
    static partial void LogNoLeader(ILogger logger, string serverName);

    [LoggerMessage(LogLevel.Warning, "{ServerName} cluster degraded: {Alive}/{Total} nodes alive")]
    static partial void LogDegraded(ILogger logger, string serverName, int alive, int total);

    [LoggerMessage(LogLevel.Debug, "{ServerName} cluster healthy: {Alive}/{Total} nodes alive")]
    static partial void LogHealthy(ILogger logger, string serverName, int alive, int total);

    #endregion // Logging
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GossipResponse))]
partial class GossipJsonContext : JsonSerializerContext;

interface IKurrentDBGossipApi {
    [Get("/gossip")]
    Task<GossipResponse> GetGossipAsync(CancellationToken ct = default);
}

sealed record GossipResponse(IReadOnlyList<GossipMember> Members);

sealed record GossipMember(
    string InstanceId,
    string State,
    bool IsAlive,
    bool IsReadOnlyReplica
);
