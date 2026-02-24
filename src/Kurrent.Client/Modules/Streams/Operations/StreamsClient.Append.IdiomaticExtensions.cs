using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<AppendRecordsSuccess> AppendRecordsAsync(
            AppendRecordsRequest request,
            CancellationToken    cancellationToken = default
        ) => client.AppendRecords(request, cancellationToken).UnwrapAsync();

        public ValueTask<AppendRecordsSuccess> AppendRecordsAsync(
            ImmutableArray<AppendRecord>     records,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords(records, checks, cancellationToken).UnwrapAsync();

        public ValueTask<AppendRecordsSuccess> AppendRecordsAsync(
            ImmutableArray<AppendRecord> records,
            ConsistencyCheck?            check             = null,
            CancellationToken            cancellationToken = default
        ) => client.AppendRecords(records, check, cancellationToken).UnwrapAsync();

        public ValueTask<AppendRecordsSuccess> AppendRecordsAsync(
            AppendRecord                     record,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords(record, checks, cancellationToken).UnwrapAsync();

        public ValueTask<AppendRecordsSuccess> AppendRecordsAsync(
            AppendRecord      record,
            ConsistencyCheck? check             = null,
            CancellationToken cancellationToken = default
        ) => client.AppendRecords(record, [check ?? ConsistencyCheck.None], cancellationToken).UnwrapAsync();
    }
}
