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

        public async ValueTask<AppendStreamSuccess> AppendAsync(
            AppendStreamRequest request,
            CancellationToken cancellationToken = default
        ) => await Append(client, request, cancellationToken).UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<AppendStreamSuccess> AppendAsync(
            StreamName stream, ExpectedStreamState expectedState, IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) => await client.AppendAsync(new AppendStreamRequest(stream, expectedState, records), cancellationToken).ConfigureAwait(false);

        public async ValueTask<AppendStreamSuccess> AppendAsync(
            StreamName stream, AppendRecord record,
            CancellationToken cancellationToken = default
        ) => await client.AppendAsync(new AppendStreamRequest(stream, ExpectedStreamState.Any ,[record]), cancellationToken).ConfigureAwait(false);
    }
}
