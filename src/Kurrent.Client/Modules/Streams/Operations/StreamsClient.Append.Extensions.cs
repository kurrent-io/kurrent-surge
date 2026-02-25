using System.Collections.Immutable;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
            ImmutableArray<AppendRecord>     records,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords(new AppendRecordsRequest(records, checks), cancellationToken);

        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
            ImmutableArray<AppendRecord> records,
            ConsistencyCheck?            check = null,
            CancellationToken            cancellationToken = default
        ) => client.AppendRecords(records, [check ?? ConsistencyCheck.None], cancellationToken);

        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
            AppendRecord                     record,
            ImmutableArray<ConsistencyCheck> checks,
            CancellationToken                cancellationToken = default
        ) => client.AppendRecords([record], checks, cancellationToken);

        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
            AppendRecord      record,
            ConsistencyCheck? check             = null,
            CancellationToken cancellationToken = default
        ) => client.AppendRecords(record, [check ?? ConsistencyCheck.None], cancellationToken);

        // public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
        //     object      value,
        //     ConsistencyCheck? check             = null,
        //     CancellationToken cancellationToken = default
        // ) => client.AppendRecords(new AppendRecord { Value = value }, check ?? ConsistencyCheck.None, cancellationToken);

        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendAdapter(
            StreamName stream,
            ExpectedStreamState expectedState,
            IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) {
            var request = new List<AppendStreamRequest> {
                new AppendStreamRequest(stream, expectedState, records)
            }.ToAsyncEnumerable();

            return client
                .AppendSession(request, cancellationToken)
                .MapAsync(value => new AppendRecordsSuccess([new StreamRevision(value[0].Stream, value[0].Revision)], value.Position))
                .MapErrorAsync(error => AppendRecordsError.FromEx(error.Value switch {
                    StreamNotFoundException ex                   => new AppendConsistencyViolationException(new StreamStateViolation(ex.Stream, expectedState, ActualStreamCondition.NoStream)),
                    StreamTombstonedException ex                 => new AppendConsistencyViolationException(new StreamStateViolation(ex.Stream, expectedState, ActualStreamCondition.Tombstoned)),
                    StreamRevisionConflictException ex           => new AppendConsistencyViolationException(new StreamStateViolation(ex.Stream, expectedState, ex.ActualRevision)),
                    MaxAppendTransactionSizeExceededException ex => ex,
                    MaxAppendRecordSizeExceededException ex      => ex,
                    _                                            => (Exception) error.Value
                }));
        }
    }
}
