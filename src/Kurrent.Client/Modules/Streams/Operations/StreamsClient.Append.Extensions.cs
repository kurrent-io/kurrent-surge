using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public async ValueTask<Result<AppendSessionSuccess, AppendSessionError>> AppendSession(
            IEnumerable<AppendStreamRequest> requests,
            CancellationToken cancellationToken = default
        ) => await client.AppendSession(requests.ToAsyncEnumerable(), cancellationToken).ConfigureAwait(false);

        public async ValueTask<Result<AppendStreamSuccess, AppendSessionError>> Append(
            AppendStreamRequest request,
            CancellationToken cancellationToken = default
        ) => await client.AppendSession([request], cancellationToken).MapAsync(ok => ok[0]).ConfigureAwait(false);

        public async ValueTask<Result<AppendStreamSuccess, AppendSessionError>> Append(
            StreamName stream, ExpectedStreamState expectedState, IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) => await client.Append(new AppendStreamRequest(stream, expectedState, records), cancellationToken).ConfigureAwait(false);

        public ValueTask<Result<AppendRecordsSuccess, AppendRecordsError>> AppendRecords(
            StreamName stream, ExpectedStreamState expectedState, IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) {
            var request = new AppendStreamRequest(stream, expectedState, records);

            return client
                .AppendSession([request], cancellationToken)
                .MapAsync(value => new AppendRecordsSuccess([(value[0].Stream, value[0].StreamRevision)], value.Position))
                .MapErrorAsync(error => AppendRecordsError.FromEx(error.Value switch {
                    StreamNotFoundException ex                   => new StreamConsistencyCheckException(ex.Stream, ActualStreamCondition.NoStream),
                    StreamTombstonedException ex                 => new StreamConsistencyCheckException(ex.Stream, ActualStreamCondition.Tombstoned),
                    StreamRevisionConflictException ex           => new StreamConsistencyCheckException(ex.Stream, ex.ActualRevision),
                    MaxAppendTransactionSizeExceededException ex => ex,
                    MaxAppendRecordSizeExceededException ex      => ex,
                    _                                            => (Exception) error.Value
                }));
        }
    }
}
