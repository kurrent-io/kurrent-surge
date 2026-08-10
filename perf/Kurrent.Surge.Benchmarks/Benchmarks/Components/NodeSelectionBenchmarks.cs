using System.Net;
using BenchmarkDotNet.Attributes;
using Kurrent.Surge.Experimental.LoadBalancing;
using KurrentDB.Client;

namespace Benchmarks;

/// <summary>
/// Prices the experimental load balancer's node ranking against the legacy selector on the
/// identical topology. The legacy path selects one endpoint per discovery; the new path ranks
/// every connectable node so the balancer gets an ordered fallback list. Both run per
/// discovery round, not per call, so this measures discovery-path overhead only.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("LoadBalancing")]
public class NodeSelectionBenchmarks {
    ClusterMessages.ClusterInfo _legacyClusterInfo = null!;
    ClusterTopology             _topology          = null!;
    NodeSelector                _legacySelector    = null!;
    Random                      _random            = null!;

    [Params(3, 10)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup() {
        var rng = new Random(42);

        var states = new[] {
            ClusterMessages.VNodeState.Leader,
            ClusterMessages.VNodeState.Follower,
            ClusterMessages.VNodeState.ReadOnlyReplica,
            ClusterMessages.VNodeState.CatchingUp
        };

        var members = Enumerable.Range(0, NodeCount)
            .Select(index => {
                // Index 0 stays a live leader so the legacy selector always finds a candidate.
                var state = index == 0 ? ClusterMessages.VNodeState.Leader : states[rng.Next(states.Length)];
                var alive = index == 0 || rng.Next(10) > 0;
                return (State: state, IsAlive: alive, Endpoint: new DnsEndPoint($"node-{index}", 2113));
            })
            .ToArray();

        _legacyClusterInfo = new(members
            .Select(member => new ClusterMessages.MemberInfo(Uuid.NewUuid(), member.State, member.IsAlive, member.Endpoint))
            .ToArray());

        _topology = new(members
            .Select(member => new ClusterNode((VNodeState)(int)member.State, member.IsAlive, member.Endpoint))
            .ToArray());

        _legacySelector = new(NodePreference.Leader);
        _random         = new(0);
    }

    [Benchmark(Baseline = true)]
    public DnsEndPoint LegacySelectNode() => _legacySelector.SelectNode(_legacyClusterInfo);

    [Benchmark]
    public object RankNodes() => NodeSelectionPolicy.RankNodes(_topology, NodePreference.Leader, _random);
}
