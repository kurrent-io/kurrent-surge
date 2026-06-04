using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

static partial class StreamsClientMapping {
    static partial class Requests {
        /// <summary>
        /// Builds a soft-delete request. When <paramref name="revision"/> is <see cref="Revision.Unset"/>,
        /// sends <c>StreamExists</c> (any-revision delete); otherwise sends the expected revision for
        /// optimistic concurrency.
        /// </summary>
        public static Contracts.DeleteReq CreateDeleteRequest(StreamName stream, Revision revision) {
            var req = new Contracts.DeleteReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        /// <summary>
        /// Builds a hard-delete (tombstone) request. Same concurrency semantics as
        /// <see cref="CreateDeleteRequest"/> — the stream name becomes permanently unusable.
        /// </summary>
        public static Contracts.TombstoneReq CreateTombstoneRequest(StreamName stream, Revision revision) {
            var req = new Contracts.TombstoneReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }
    }

    /// <summary>
    /// Maps a V1 <c>DeleteResp.Position</c> to a <see cref="LogPosition"/>.
    /// Returns <see cref="LogPosition.Unset"/> when the server omits the position
    /// (e.g. soft-delete of a stream that was already deleted).
    /// </summary>
    public static LogPosition MapToLogPosition(this Contracts.DeleteResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };

    /// <summary>
    /// Maps a V1 <c>TombstoneResp.Position</c> to a <see cref="LogPosition"/>.
    /// Returns <see cref="LogPosition.Unset"/> when the server omits the position.
    /// </summary>
    public static LogPosition MapToLogPosition(this Contracts.TombstoneResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };
}
