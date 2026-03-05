using Kurrent.Client.Exceptions;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {

    [Obsolete("Use AppendRecords instead.")]
    async ValueTask<AppendSessionSuccess> AppendSession(
        IAsyncEnumerable<AppendStreamRequest> requests, CancellationToken cancellationToken = default) {
        try {
            using var session = ServiceClient.AppendSession(cancellationToken: cancellationToken);

            await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (request.Stream == StreamName.None || request.Stream == StreamName.All)
                    throw new ArgumentException("Stream cannot be StreamName.None or StreamName.All", nameof(request.Stream));

                var records = await request.Messages
                    .Map(request.Stream, SerializerProvider, cancellationToken)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!records.Any())
                    throw new ArgumentException("At least one record must be provided", nameof(records));

                var serviceRequest = new Contracts.AppendRequest {
                    Stream           = request.Stream,
                    ExpectedRevision = request.ExpectedState.ToWireValue(),
                    Records          = { records }
                };

                // Cancellation of stream writes is not supported by this gRPC implementation.
                // To cancel the operation, we should cancel the entire session.
                await session.RequestStream
                    .WriteAsync(serviceRequest, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await session.RequestStream
                .CompleteAsync()
                .ConfigureAwait(false);

            var response = await session.ResponseAsync
                .ConfigureAwait(false);

            return response.Map();
        }
        catch (Exception ex) {
            throw ex.ToException(StreamNotFoundException.From, StreamTombstonedException.From, StreamRevisionConflictException.From, StreamAlreadyInAppendSessionException.From, MaxAppendRecordSizeExceededException.From, MaxAppendTransactionSizeExceededException.From, AppendSessionNoRequestsException.From);
        }
    }

    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        StreamName                stream,
        ExpectedStreamState       expectedState,
        IEnumerable<AppendRecord> records,
        CancellationToken         cancellationToken = default
    ) {
        var request = new List<AppendStreamRequest> {
            new AppendStreamRequest(stream, expectedState, records)
        }.ToAsyncEnumerable();

        try {
            var value = await AppendSession(request, cancellationToken)
                .ConfigureAwait(false);

            return new AppendRecordsSuccess(
                [new StreamRevision(value[0].Stream, value[0].Revision)],
                value.Position);
        }
        catch (StreamNotFoundException ex) {
            throw new AppendConsistencyViolationException(
                new StreamStateViolation(ex.Stream, expectedState, ActualStreamCondition.NoStream));
        }
        catch (StreamTombstonedException ex) {
            throw new AppendConsistencyViolationException(
                new StreamStateViolation(ex.Stream, expectedState, ActualStreamCondition.Tombstoned));
        }
        catch (StreamRevisionConflictException ex) {
            throw new AppendConsistencyViolationException(
                new StreamStateViolation(ex.Stream, expectedState, ex.ActualRevision));
        }
    }

    public async ValueTask<AppendRecordsSuccess> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var response = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToModel(response);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
