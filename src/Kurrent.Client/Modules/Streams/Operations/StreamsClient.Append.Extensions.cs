namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(AppendRecordsRequestBuilder builder) {
        public async ValueTask<AppendRecordsResponse> ExecuteAsync(
            StreamsClient client,
            CancellationToken cancellationToken = default
        ) => await client.AppendRecords(builder.Build(), cancellationToken).ConfigureAwait(false);
    }
}
