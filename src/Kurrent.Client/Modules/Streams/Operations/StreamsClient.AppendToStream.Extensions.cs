namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(AppendToStreamRequestBuilder builder) {
        public async ValueTask<AppendToStreamResponse> ExecuteAsync(
            StreamsClient client,
            CancellationToken cancellationToken = default
        ) => await client.AppendToStream(builder.Build(), cancellationToken).ConfigureAwait(false);
    }
}
