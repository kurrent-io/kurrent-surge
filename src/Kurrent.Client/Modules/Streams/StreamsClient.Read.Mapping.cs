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
        /// Creates a request to read log records based on the provided options.
        /// </summary>
        public static Contracts.ReadReq CreateReadRecordsRequest(ReadRecordsRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = (ulong)options.Limit,
                ResolveLinks  = options.ResolveLinks,
            }};

            // Stream branch uses Revision.Min — read whole stream from beginning when
            // the filter resolves to a StreamNameFilter. Log branch uses options.Start.
            var start = RecordPosition.From(options.Start, StreamName.Unset, Revision.Min);
            RouteByFilter(req.Options, options.Filter, start);

            return req;
        }

        /// <summary>
        /// Creates a request to read records from a specific stream based on the provided options.
        /// Filter is source-of-truth for routing: if no filter is supplied, a <see cref="StreamNameFilter"/>
        /// is synthesized from <see cref="ReadStreamRequest.Stream"/>. Otherwise the caller's filter wins
        /// (the validator ensures any explicit <see cref="StreamNameFilter"/> targets the same stream).
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamRequest(ReadStreamRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = (ulong)options.Limit,
                ResolveLinks  = options.ResolveLinks,
            }};

            // Empty filter → synthesize a StreamNameFilter from the explicit stream property.
            // Any other filter wins outright.
            var filter = options.Filter is EmptyFilter
                ? StreamNameFilter.Parse(options.Stream)
                : options.Filter;

            // Stream branch uses options.Start (Revision). LogPosition.Earliest is a safe
            // placeholder for the $all branch — never reached when filter is StreamNameFilter.
            var start = RecordPosition.From(LogPosition.Earliest, options.Stream, options.Start);
            
            RouteByFilter(req.Options, filter, start);

            return req;
        }
    }
}
