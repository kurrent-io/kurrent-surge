namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public async ValueTask<AppendSessionSuccess> AppendSessionAsync(
            IAsyncEnumerable<AppendStreamRequest> requests, CancellationToken cancellationToken = default
        ) => await client.AppendSession(requests, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<AppendSessionSuccess> AppendSessionAsync(
            IEnumerable<AppendStreamRequest> requests,
            CancellationToken cancellationToken = default
        ) => await AppendSession(client, requests, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<AppendStreamSuccess> AppendStreamAsync(
            AppendStreamRequest request,
            CancellationToken cancellationToken = default
        ) => await AppendStream(client, request, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<AppendStreamSuccess> AppendStreamAsync(
            StreamName stream, ExpectedStreamState expectedState, IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) => await client.AppendStreamAsync(new AppendStreamRequest(stream, expectedState, records), cancellationToken).ConfigureAwait(false);
    }
}
