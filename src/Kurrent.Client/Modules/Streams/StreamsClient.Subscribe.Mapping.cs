using Kurrent.Client.Streams.Checkpoints;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

static partial class StreamsClientMapping {
    internal static partial class Requests {
        /// <summary>
        /// Builds a subscription request. Dispatches between a single-stream subscription and a
        /// filtered <c>$all</c> subscription based on the unwrapped filter. For filtered <c>$all</c>,
        /// the server emits periodic <see cref="IntervalCheckpoint"/> barriers paced by
        /// <paramref name="maxSearchWindow"/>.
        /// </summary>
        public static Contracts.ReadReq CreateSubscriptionRequest(
            RecordFilter filter,
            RecordPosition start,
            bool resolveLinks,
            uint maxSearchWindow = DefaultMaxSearchWindow
        ) {
            var req = new Contracts.ReadReq { Options = new() {
                ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                Subscription  = DefaultSubscriptionOptions,
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ResolveLinks  = resolveLinks,
            }};

            RouteByFilter(req.Options, filter, start, maxSearchWindow);
            return req;
        }
    }
}
