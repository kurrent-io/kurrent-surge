using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        // Belt-and-suspenders: validate every record has a stream
        var missingStream = request.Records.FirstOrDefault(r => r.Stream == StreamName.Unset);
        if (missingStream is not null)
            throw new InvalidOperationException(
                $"Record '{missingStream.RecordId}' has no stream assigned. Set the stream on each record or use AppendToStream for single-stream appends.");

        try {
            request = request with {
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

            return Mapper.ToAppendRecordsResponse(response);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
