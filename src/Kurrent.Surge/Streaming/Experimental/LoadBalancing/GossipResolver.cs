using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using KurrentDB.Client;

namespace Kurrent.Surge.Experimental.LoadBalancing;

sealed record GossipResolverOptions {
    public required DnsEndPoint[] Seeds { get; init; }

    public NodeReadPreference NodeReadPreference  { get; init; } = NodeReadPreference.Leader;
    public int                MaxDiscoverAttempts { get; init; } = 10;
    public TimeSpan           DiscoveryInterval   { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// When false the resolver publishes the seeds verbatim without querying gossip.
    /// Used for direct single-node connections, which still follow leader redirects.
    /// </summary>
    public bool UseGossip { get; init; } = true;

    public Random? Random { get; init; }
}

/// <summary>
/// Resolves cluster addresses through gossip and publishes them ranked by node preference.
/// Rank 0 is the routing target; the rest are ready fallbacks for the balancer.
/// </summary>
sealed partial class GossipResolver : PollingResolver {
    internal static readonly BalancerAttributesKey<int> RankKey = new("kurrent-node-rank");

    readonly GossipResolverOptions   _options;
    readonly IGossipClient           _gossip;
    readonly ILogger<GossipResolver> _log;
    readonly Random                  _random;
    readonly Lock                    _lock = new();

    DnsEndPoint[]  _knownEndpoints;
    DnsEndPoint[]  _lastPublished = [];
    DnsEndPoint?   _pendingLeader;

    public GossipResolver(GossipResolverOptions options, IGossipClient gossip, ILoggerFactory loggerFactory)
        : base(loggerFactory) {
        _options        = options;
        _gossip         = gossip;
        _random         = options.Random ?? Random.Shared;
        _knownEndpoints = options.Seeds;
        _log            = loggerFactory.CreateLogger<GossipResolver>();
    }

    /// <summary>
    /// Routes traffic to the advertised leader without a gossip round-trip, matching the legacy
    /// NewLeader reconnection: the next publish ranks that endpoint first and keeps the previously
    /// published endpoints as fallbacks. Discovery resumes on later refreshes.
    /// </summary>
    public void AnnounceLeader(DnsEndPoint leader) {
        lock (_lock) _pendingLeader = leader;
        Refresh();
    }

    protected override async Task ResolveAsync(CancellationToken cancellationToken) {
        DnsEndPoint? leader;
        lock (_lock) {
            leader         = _pendingLeader;
            _pendingLeader = null;
        }

        if (leader is not null) {
            PublishLeaderFirst(leader);
            return;
        }

        if (!_options.UseGossip) {
            Publish(_options.Seeds);
            return;
        }

        await DiscoverAsync().ConfigureAwait(false);

        void PublishLeaderFirst(DnsEndPoint endpoint) {
            DnsEndPoint[] lastPublished;
            lock (_lock) lastPublished = _lastPublished;

            var fallbacks = (lastPublished.Length > 0 ? lastPublished : _options.Seeds)
                .Where(fallback => !DnsEndPointComparer.Instance.Equals(fallback, endpoint));

            Publish([endpoint, ..fallbacks]);
        }

        async Task DiscoverAsync() {
            var attempt = 0;

            while (true) {
                attempt++;

                foreach (var candidate in Shuffled(SnapshotKnownEndpoints())) {
                    try {
                        var topology = await _gossip.GetTopology(candidate, cancellationToken).ConfigureAwait(false);
                        var ranked   = NodeSelectionPolicy.RankNodes(topology, _options.NodeReadPreference, _random);

                        if (ranked.Count == 0)
                            throw new InvalidOperationException("No nodes are in a connectable state.");

                        // Gossip succeeded: from now on query exactly the current members, not the seeds.
                        lock (_lock)
                            _knownEndpoints = topology.Nodes.Select(node => node.Endpoint).ToArray();

                        LogDiscovered(_log, ranked[0].Endpoint.Host, ranked[0].Endpoint.Port);

                        Publish(ranked.Select(node => node.Endpoint).ToArray());
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        throw;
                    }
                    catch (Exception ex) {
                        LogDiscoveryCandidateFailed(_log, GetLogLevelForAttempt(), ex, candidate.Host, candidate.Port);
                    }
                }

                if (_options.MaxDiscoverAttempts >= 0 && attempt >= _options.MaxDiscoverAttempts)
                    break;

                // Nothing answered this round: fall back to the seeds and try again.
                lock (_lock) _knownEndpoints = _options.Seeds;

                await Task.Delay(_options.DiscoveryInterval, cancellationToken).ConfigureAwait(false);
            }

            LogDiscoveryFailed(_log, _options.MaxDiscoverAttempts);

            Listener(ResolverResult.ForFailure(new(
                StatusCode.Unavailable,
                $"Failed to discover a candidate node in {_options.MaxDiscoverAttempts} attempts.")));

            DnsEndPoint[] SnapshotKnownEndpoints() {
                lock (_lock) return _knownEndpoints;
            }

            DnsEndPoint[] Shuffled(DnsEndPoint[] endpoints) {
                var copy = endpoints.ToArray();
                lock (_random) _random.Shuffle(copy);
                return copy;
            }

            LogLevel GetLogLevelForAttempt() =>
                attempt switch {
                    _ when attempt == _options.MaxDiscoverAttempts => LogLevel.Error,
                    1                                              => LogLevel.Warning,
                    _                                              => LogLevel.Debug
                };
        }
    }

    void Publish(DnsEndPoint[] endpoints) {
        lock (_lock) _lastPublished = endpoints;

        var addresses = endpoints
            .Select((endpoint, rank) => {
                var address = new BalancerAddress(endpoint.Host, endpoint.Port);
                address.Attributes.Set(RankKey, rank);
                return address;
            })
            .ToArray();

        Listener(ResolverResult.ForResult(addresses));
    }

    #region ->> Logging <<-

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully discovered candidate at {Host}:{Port}.")]
    static partial void LogDiscovered(ILogger logger, string host, int port);

    [LoggerMessage(Message = "Could not discover candidate from {Host}:{Port}.")]
    static partial void LogDiscoveryCandidateFailed(ILogger logger, LogLevel level, Exception ex, string host, int port);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to discover candidate in {MaxDiscoverAttempts} attempts.")]
    static partial void LogDiscoveryFailed(ILogger logger, int maxDiscoverAttempts);

    #endregion
}
