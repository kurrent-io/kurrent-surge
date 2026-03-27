using System.Collections.Immutable;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<AppendRecordsSuccess> AppendRecords(
            ImmutableArray<AppendRecord>     records,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords(new AppendRecordsRequest(records, checks), cancellationToken);

        public ValueTask<AppendRecordsSuccess> AppendRecords(
            ImmutableArray<AppendRecord> records,
            ConsistencyCheck?            check = null,
            CancellationToken            cancellationToken = default
        ) => client.AppendRecords(records, [check ?? ConsistencyCheck.None], cancellationToken);

        /// <summary>
        /// The record is appended to the stream with the given consistency checks.
        /// </summary>
        public ValueTask<AppendRecordsSuccess> AppendRecords(
            AppendRecord                     record,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords([record], checks, cancellationToken);

        /// <summary>
        /// The record is appended to the stream with the given consistency checks.
        /// </summary>
        public ValueTask<AppendRecordsSuccess> AppendRecords(
            AppendRecord      record,
            ConsistencyCheck? check             = null,
            CancellationToken cancellationToken = default
        ) => client.AppendRecords(record, [check ?? ConsistencyCheck.None], cancellationToken);
    }
}
