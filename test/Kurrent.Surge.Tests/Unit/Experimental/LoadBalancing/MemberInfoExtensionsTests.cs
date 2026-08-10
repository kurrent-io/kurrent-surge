using EventStore.Client.Gossip;
using Kurrent.Surge.Experimental.LoadBalancing;
using VNodeState = EventStore.Client.Gossip.MemberInfo.Types.VNodeState;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

public class MemberInfoExtensionsTests {
    // Explicit per-state table with no default arm: a new wire state fails the coverage
    // assertion below until it is classified here deliberately.
    static readonly Dictionary<VNodeState, NodeRole> ExpectedRoles = new() {
        [VNodeState.Leader]             = NodeRole.Leader,
        [VNodeState.Follower]           = NodeRole.Follower,
        [VNodeState.ReadOnlyReplica]    = NodeRole.Replica,
        [VNodeState.ReadOnlyLeaderless] = NodeRole.Replica,
        [VNodeState.Initializing]       = NodeRole.NotAllowed,
        [VNodeState.DiscoverLeader]     = NodeRole.NotAllowed,
        [VNodeState.Unknown]            = NodeRole.NotAllowed,
        [VNodeState.PreReplica]         = NodeRole.NotAllowed,
        [VNodeState.CatchingUp]         = NodeRole.NotAllowed,
        [VNodeState.Clone]              = NodeRole.NotAllowed,
        [VNodeState.PreLeader]          = NodeRole.NotAllowed,
        [VNodeState.Manager]            = NodeRole.NotAllowed,
        [VNodeState.ShuttingDown]       = NodeRole.NotAllowed,
        [VNodeState.Shutdown]           = NodeRole.NotAllowed,
        [VNodeState.PreReadOnlyReplica] = NodeRole.NotAllowed,
        [VNodeState.ResigningLeader]    = NodeRole.NotAllowed
    };

    [Test]
    public async ValueTask classifies_every_wire_state(CancellationToken cancellationToken) {
        // Arrange
        var allStates = Enum.GetValues<VNodeState>();

        // Assert: the table is exhaustive — a new wire state must be classified above first.
        await Assert.That(ExpectedRoles.Keys.Order().SequenceEqual(allStates.Order())).IsTrue();

        // Act + Assert: the mapping the gossip path runs agrees with the table.
        foreach (var state in allStates) {
            var node = new MemberInfo {
                State        = state,
                IsAlive      = true,
                HttpEndPoint = new() { Address = "node", Port = 2113 }
            }.ToClusterNode();

            await Assert.That(node.Role).IsEqualTo(ExpectedRoles[state]);
        }
    }
}
