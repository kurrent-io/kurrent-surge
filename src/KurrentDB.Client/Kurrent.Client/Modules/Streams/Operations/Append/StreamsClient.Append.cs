using Kurrent.Client.Exceptions;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<Result<AppendSessionSuccess, AppendSessionError>> AppendSession(
        IAsyncEnumerable<AppendStreamRequest> requests, CancellationToken cancellationToken = default) {
        try {
            using var session = ServiceClient.AppendSession(cancellationToken: cancellationToken);

            await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var records = await request.Messages
                    .Map(request.Stream, SerializerProvider, cancellationToken)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                var serviceRequest = new Contracts.AppendRequest {
                    Stream           = request.Stream,
                    ExpectedRevision = request.ExpectedState,
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
            return AppendSessionError.FromEx(ex);
        }
    }
}
