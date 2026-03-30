using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<AppendRecordsResponse> AppendRecords(
        AppendRecordsRequest request,
        CancellationToken cancellationToken = default
    ) {
        // Validate all records have streams and meet invariants
        request.EnsureValid();

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

            var rsp = await ServiceClient
                .AppendRecordsAsync(serviceRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Mapper.ToAppendRecordsResponse(rsp);
        }
        catch (Exception ex) {
            throw ex.ToException(
                AppendConsistencyViolationException.From,
                MaxAppendRecordSizeExceededException.From,
                MaxAppendTransactionSizeExceededException.From);
        }
    }
}
