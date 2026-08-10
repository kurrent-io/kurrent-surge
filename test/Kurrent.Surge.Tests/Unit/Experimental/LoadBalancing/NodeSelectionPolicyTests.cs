using System.Net;
using Kurrent.Surge.Experimental.LoadBalancing;
using KurrentDB.Client;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

public class NodeSelectionPolicyTests {
    // The connectable states under the legacy rules: everything else is filtered out.
    static readonly VNodeState[] ConnectableStates = [
        VNodeState.Leader,
        VNodeState.Follower,
        VNodeState.ReadOnlyReplica,
        VNodeState.ReadOnlyLeaderless
    ];

    [Test]
    [PreferenceOrderingCases]
    public async ValueTask ranks_connectable_nodes_by_preference(NodeReadPreference preference, string expectedOrder) {
        // Arrange: the expected order names states because the state enum is internal
        // and cannot appear in a public test signature.
        var topology = TopologyOf(ConnectableStates);

        // Act
        var ranked = NodeSelectionPolicy.RankNodes(topology, preference);

        // Assert
        await Assert.That(string.Join(",", ranked.Select(node => node.State))).IsEqualTo(expectedOrder);
    }

    [Test]
    public async ValueTask filters_out_nodes_in_not_allowed_states(CancellationToken cancellationToken) {
        // Arrange: one alive node in every state that exists on the wire.
        var topology = TopologyOf(Enum.GetValues<VNodeState>());
        var expected = string.Join(",", ConnectableStates.Order());

        // Act
        var ranked = NodeSelectionPolicy.RankNodes(topology, NodeReadPreference.Random);

        // Assert
        await Assert.That(string.Join(",", ranked.Select(node => node.State).Order())).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask filters_out_dead_nodes(CancellationToken cancellationToken) {
        // Arrange
        var deadLeader = new ClusterNode(VNodeState.Leader, IsAlive: false, new("leader", 2113));
        var follower   = new ClusterNode(VNodeState.Follower, IsAlive: true, new("follower", 2113));
        var topology   = new ClusterTopology([deadLeader, follower]);

        // Act
        var ranked = NodeSelectionPolicy.RankNodes(topology, NodeReadPreference.Leader);

        // Assert
        await Assert.That(ranked).HasCount().EqualTo(1);
        await Assert.That(ranked[0]).IsEqualTo(follower);
    }

    [Test]
    public async ValueTask keeps_all_candidates_when_preference_is_random(CancellationToken cancellationToken) {
        // Arrange
        var topology = TopologyOf(ConnectableStates);
        var expected = string.Join(",", ConnectableStates.Order());

        // Act
        var ranked = NodeSelectionPolicy.RankNodes(topology, NodeReadPreference.Random);

        // Assert
        await Assert.That(string.Join(",", ranked.Select(node => node.State).Order())).IsEqualTo(expected);
    }

    static ClusterTopology TopologyOf(IEnumerable<VNodeState> states) =>
        new(states
            .Select((state, index) => new ClusterNode(state, IsAlive: true, new($"node-{index}", 2113)))
            .ToArray());

    public sealed class PreferenceOrderingCases : TestCaseDataGenerator<(NodeReadPreference Preference, string ExpectedOrder)> {
        protected override IEnumerable<TestCase<(NodeReadPreference Preference, string ExpectedOrder)>> Data() {
            yield return new((NodeReadPreference.Leader, "Leader,Follower,ReadOnlyReplica,ReadOnlyLeaderless"), "leader preference");
            yield return new((NodeReadPreference.Follower, "Follower,Leader,ReadOnlyReplica,ReadOnlyLeaderless"), "follower preference");
            yield return new((NodeReadPreference.ReadOnlyReplica, "ReadOnlyReplica,ReadOnlyLeaderless,Follower,Leader"), "read-only replica preference");
        }
    }
}
