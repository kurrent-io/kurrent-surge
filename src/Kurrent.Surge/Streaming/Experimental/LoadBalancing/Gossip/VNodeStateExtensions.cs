using VNodeState = EventStore.Client.Gossip.MemberInfo.Types.VNodeState;

namespace Kurrent.Surge.Experimental.LoadBalancing;

static class VNodeStateExtensions {
    extension(VNodeState state) {
        /// <summary>
        /// Maps a wire state to its routing role. Total by design: states this client does
        /// not recognize map to <see cref="NodeRole.NotAllowed"/>, so a future server state
        /// stays unroutable until it is classified deliberately.
        /// </summary>
        public NodeRole ToNodeRole() =>
            state switch {
                VNodeState.Leader   => NodeRole.Leader,
                VNodeState.Follower => NodeRole.Follower,

                VNodeState.ReadOnlyReplica or
                VNodeState.ReadOnlyLeaderless => NodeRole.Replica,

                _ => NodeRole.NotAllowed
            };
    }
}