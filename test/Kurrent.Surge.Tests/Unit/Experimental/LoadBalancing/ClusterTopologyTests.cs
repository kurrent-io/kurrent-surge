using Kurrent.Surge.Client.LoadBalancing;
using KurrentDB.Client;

namespace Kurrent.Surge.Tests.Client.LoadBalancing;

public class ClusterTopologyTests {
    static readonly NodeRole[] ConnectableRoles = [NodeRole.Leader, NodeRole.Follower, NodeRole.Replica];

    [Test]
    [PreferenceOrderingCases]
    public async ValueTask ranks_connectable_nodes_by_preference(NodeReadPreference preference, string expectedOrder) {
        // Arrange
        var topology = TopologyOf(ConnectableRoles);

        // Act
        var ranked = topology.RankNodes(preference);

        // Assert
        await Assert.That(string.Join(",", ranked.Select(node => node.Role))).IsEqualTo(expectedOrder);
    }

    [Test]
    public async ValueTask filters_out_not_allowed_nodes(CancellationToken cancellationToken) {
        // Arrange
        var notAllowed = new ClusterNode(NodeRole.NotAllowed, IsAlive: true, new("blocked", 2113));
        var follower   = new ClusterNode(NodeRole.Follower, IsAlive: true, new("follower", 2113));
        var topology   = new ClusterTopology([notAllowed, follower]);

        // Act
        var ranked = topology.RankNodes(NodeReadPreference.Leader);

        // Assert
        await Assert.That(ranked).HasCount().EqualTo(1);
        await Assert.That(ranked[0]).IsEqualTo(follower);
    }

    [Test]
    public async ValueTask filters_out_dead_nodes(CancellationToken cancellationToken) {
        // Arrange
        var deadLeader = new ClusterNode(NodeRole.Leader, IsAlive: false, new("leader", 2113));
        var follower   = new ClusterNode(NodeRole.Follower, IsAlive: true, new("follower", 2113));
        var topology   = new ClusterTopology([deadLeader, follower]);

        // Act
        var ranked = topology.RankNodes(NodeReadPreference.Leader);

        // Assert
        await Assert.That(ranked).HasCount().EqualTo(1);
        await Assert.That(ranked[0]).IsEqualTo(follower);
    }

    [Test]
    public async ValueTask keeps_all_candidates_when_preference_is_random(CancellationToken cancellationToken) {
        // Arrange
        var topology = TopologyOf(ConnectableRoles);
        var expected = string.Join(",", ConnectableRoles.Order());

        // Act
        var ranked = topology.RankNodes(NodeReadPreference.Random);

        // Assert
        await Assert.That(string.Join(",", ranked.Select(node => node.Role).Order())).IsEqualTo(expected);
    }

    static ClusterTopology TopologyOf(IEnumerable<NodeRole> roles) =>
        new(roles
            .Select((role, index) => new ClusterNode(role, IsAlive: true, new($"node-{index}", 2113)))
            .ToArray());

    public sealed class PreferenceOrderingCases : TestCaseDataGenerator<(NodeReadPreference Preference, string ExpectedOrder)> {
        protected override IEnumerable<TestCase<(NodeReadPreference Preference, string ExpectedOrder)>> Data() {
            yield return new((NodeReadPreference.Leader, "Leader,Follower,Replica"), "leader preference");
            yield return new((NodeReadPreference.Follower, "Follower,Leader,Replica"), "follower preference");
            yield return new((NodeReadPreference.ReadOnlyReplica, "Replica,Follower,Leader"), "read-only replica preference");
        }
    }
}
