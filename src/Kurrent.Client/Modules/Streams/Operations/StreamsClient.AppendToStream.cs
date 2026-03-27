using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        CancellationToken cancellationToken = default
    ) {
        try {
            // Belt-and-suspenders: stamp stream on all records and filter checks
            request = request with {
                Records = [..request.Records.Select(r => r with { Stream = request.Stream })],
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
            throw v.ActualState.Match<Exception>(
                condition => condition switch {
                    ActualStreamCondition.Tombstoned => new StreamTombstonedException(v.Stream),
                    ActualStreamCondition.NoStream   => new StreamNotFoundException(v.Stream),
                    _                                => ex
                },
                _ => ex);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
