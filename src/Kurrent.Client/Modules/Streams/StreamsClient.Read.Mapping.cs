#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

static partial class StreamsClientMapping {
    internal static partial class Requests {
        /// <summary>
        /// Builds a single-record read at a specific log position with link resolution enabled.
        /// Used for inspecting an individual record by its commit position.
        /// Reads backwards from <see cref="LogPosition.Latest"/>, forwards otherwise.
        /// </summary>
        public static Contracts.ReadReq CreateInspectRecordRequest(LogPosition position) {
            return new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = 1,
                    ResolveLinks  = true,
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
        /// Creates a request to read records based on the provided options. The start arrives
        /// pre-resolved by the client routing (see <c>StreamsClient.ReadRecordsCore</c>):
        /// <see cref="RouteByFilter"/> consumes the half matching the branch it picks —
        /// the revision for a stream target, the log position for the log.
        /// </summary>
        public static Contracts.ReadReq CreateReadRecordsRequest(ReadRecordsOptions options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = (ulong)options.Limit,
                ResolveLinks  = options.ResolveLinks,
            }};

            RouteByFilter(req.Options, options.Filter, options.Start, (uint)options.FilterMaxSearchWindow);

            return req;
        }
    }
}
