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
    }
}
