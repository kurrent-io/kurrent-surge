using Google.Rpc;
using Kurrent.Surge.Errors;
using Status = Google.Rpc.Status;

using static Kurrent.Surge.Client.Exceptions.MatcherHelpers;

namespace Kurrent.Surge.Client.Exceptions;

static class ServerNotReadyMatcher {
    extension(ServerNotReadyException) {
        public static ServerNotReadyException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ServerNotReadyException.Code, (d, m, i) => new ServerNotReadyException(d, m, i));
    }
}

static class ServerOverloadedMatcher {
    extension(ServerOverloadedException) {
        public static ServerOverloadedException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, ServerOverloadedException.Code, (d, m, i) => new ServerOverloadedException(d, m, i));
    }
}

static class LeaderForwardingTimeoutMatcher {
    extension(LeaderForwardingTimeoutException) {
        public static LeaderForwardingTimeoutException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, LeaderForwardingTimeoutException.Code, (d, m, i) => new LeaderForwardingTimeoutException(d, m, i));
    }
}

static class NotLeaderNodeMatcher {
    extension(NotLeaderNodeException) {
        public static NotLeaderNodeException? Match(Status status, ErrorInfo? errorInfo) =>
            OnReason(status, errorInfo, NotLeaderNodeException.Code, (d, m, i) => {
                // The server can send an empty or non-GUID NodeId (observed on not-leader redirects);
                // the endpoint is what matters for reconnection, so tolerate it as Guid.Empty.
                var info = status.GetDetail<Rpc.NotLeaderNodeErrorDetails>()?.CurrentLeader switch {
                    { Host.Length: > 0 } leader => NodeInfo.From(
                        new(leader.Host, leader.Port),
                        Guid.TryParse(leader.NodeId, out var nodeId) ? nodeId : Guid.Empty),
                    _ => NodeInfo.None
                };
                
                return new NotLeaderNodeException(d, m, i) { CurrentLeader = info };
            });
    }
}
