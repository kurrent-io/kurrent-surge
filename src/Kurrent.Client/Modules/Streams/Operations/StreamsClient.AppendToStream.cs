using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            // Validate all records and stamp target stream
            request = request.EnsureValid() with {
                Checks = [
                    ..request.Checks
                        .Where(c => c.IsStreamStateCheck)
                        .Where(c => c.Value is StreamStateCheck check && check.ExpectedState != ExpectedStreamState.Any)
                ]
            };

            var serviceRequest = await Mapper
                .ToContract(request, cancellationToken)
                .ConfigureAwait(false);

            var response = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToAppendToStreamResponse(response, request.Stream);
        }
        catch (AppendConsistencyViolationException ex) when (ex.Violations is [{ Value: StreamStateViolation v }]) {
            // need to check for revision mismatch and stream state violations separately since they are both returned as consistency violations by the service
            // if both expected and actual state are revision violations and different, we return a stream revision conflict exception

            if (v.ActualState.Value is Revision revision)
                throw new StreamRevisionConflictException(v.Stream, v.ExpectedState, revision);

            if (v.ExpectedState.IsRevision && v.ActualState.IsRevision && v.ExpectedState.AsRevision != v.ActualState.AsRevision)
                throw new StreamRevisionConflictException(v.Stream, v.ExpectedState.AsRevision, v.ActualState.AsRevision);
            else {
                throw v.ActualState.Match<Exception>(
                    condition => condition switch {
                        ActualStreamCondition.Tombstoned => new StreamTombstonedException(v.Stream),
                        ActualStreamCondition.NoStream   => new StreamNotFoundException(v.Stream),
                        _                                => ex
                    },
                    _ => ex);
            }
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
