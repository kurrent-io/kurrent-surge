#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Surge.Client.Streams;

static partial class StreamsClientMapping {
    internal static partial class Requests {
        /// <summary>
        /// Builds a single-record read at a specific log position with link resolution disabled.
        /// Used for inspecting the individual record physically at that commit position.
        /// Reads backwards from <see cref="LogPosition.Latest"/>, forwards otherwise.
        /// </summary>
        public static Contracts.ReadReq CreateInspectRecordRequest(LogPosition position) {
            return new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = 1,
                    // Inspecting a log position addresses one physical record; resolving links would
                    // return a different record (the link's target) at a different position.
                    ResolveLinks  = false,
                    All           = ConvertToAllOptions(position),
                    ReadDirection = position == LogPosition.Latest
                        ? Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards
                        : Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                }
            };
        }

        /// <summary>
        /// Builds a read from the edge of a stream — head (backwards) or tail (forwards).
        /// Used for stream info probes like finding the last or first event.
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamEdgeRequest(StreamName stream, ReadDirection direction, long? count = null) {
            return new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    // A stream edge is the record physically at that stream's head/tail; resolving
                    // links would substitute a target event from another stream.
                    ResolveLinks  = false,
                    ReadDirection = ToReadDirection(direction),
                    Stream = ConvertToStreamOptions(
                        stream, start: direction switch {
                            ReadDirection.Backwards => Revision.Max,
                            ReadDirection.Forwards  => Revision.Min,
                        }
                    ),
                }
            };
        }

        /// <summary>
        /// Creates a request to read records based on the provided options. <paramref name="start"/>
        /// arrives pre-resolved by the client routing (see <c>StreamsClient.ReadRecordsCore</c>):
        /// <see cref="RouteByFilter"/> consumes the half matching the branch it picks —
        /// the revision for a stream target, the log position for the log. The wire count is
        /// unbounded: <c>Limit</c> is applied by the caller after client-side filtering, and
        /// disposing the call ends the read.
        /// </summary>
        public static Contracts.ReadReq CreateReadRecordsRequest(ReadRecordsOptions options, RecordPosition start) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = long.MaxValue,
            }};

            var targetsStream = RouteByFilter(req.Options, options.Filter, start, (uint)options.FilterMaxSearchWindow);
            req.Options.ResolveLinks = targetsStream;

            return req;
        }
    }
}
