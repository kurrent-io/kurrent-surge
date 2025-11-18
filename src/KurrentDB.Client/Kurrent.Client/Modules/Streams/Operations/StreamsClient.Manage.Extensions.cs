namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Result<LogPosition, DeleteStreamError>> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Delete(stream, StreamRevision.Unset, cancellationToken);

        public ValueTask<Result<LogPosition, TombstoneStreamError>> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, StreamRevision.Unset, cancellationToken);

        public ValueTask<Result<StreamRevision, SetStreamMetadataError>> SetStreamMetadata(StreamName stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            client.SetStreamMetadata(stream, metadata, StreamRevision.Unset, cancellationToken);

        public ValueTask<Result<StreamRevision, SetStreamMetadataError>> ConfigureStreamDataRetention(StreamName stream, StreamDataRetentionOptions options, CancellationToken cancellationToken) =>
            client.ConfigureStreamDataRetention(stream, options, StreamRevision.Unset, cancellationToken);
    }
}

public static partial class StreamsClientIdiomaticExtensions {
    extension(StreamsClient client) {
        public ValueTask<LogPosition> DeleteAsync(StreamName stream, StreamRevision revision, CancellationToken cancellationToken = default) =>
            client.Delete(stream, revision, cancellationToken).UnwrapAsync();

        public ValueTask<LogPosition> TombstoneAsync(StreamName stream, StreamRevision revision, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, revision, cancellationToken).UnwrapAsync();

        public ValueTask<LogPosition> DeleteAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            DeleteAsync(client, stream, StreamRevision.Unset, cancellationToken);

        public ValueTask<LogPosition> TombstoneAsync(StreamName stream, CancellationToken cancellationToken = default) =>
            TombstoneAsync(client, stream, StreamRevision.Unset, cancellationToken);

        public ValueTask<StreamRevision> SetStreamMetadataAsync(StreamName stream, StreamMetadata metadata, StreamRevision expectedRevision, CancellationToken cancellationToken = default) =>
            client.SetStreamMetadata(stream, metadata, expectedRevision, cancellationToken).UnwrapAsync();

        public ValueTask<StreamRevision> SetStreamMetadataAsync(StreamName stream, StreamMetadata metadata, CancellationToken cancellationToken = default) =>
            SetStreamMetadataAsync(client, stream, metadata, StreamRevision.Unset, cancellationToken);

        public ValueTask<StreamRevision> ConfigureStreamDataRetentionAsync(StreamName stream, StreamDataRetentionOptions options, StreamRevision metaStreamRevision, CancellationToken cancellationToken) =>
            client.ConfigureStreamDataRetention(stream, options, metaStreamRevision, cancellationToken).UnwrapAsync();

        public ValueTask<StreamRevision> ConfigureStreamDataRetentionAsync(StreamName stream, StreamDataRetentionOptions options, CancellationToken cancellationToken) =>
            ConfigureStreamDataRetentionAsync(client, stream, options, StreamRevision.Unset, cancellationToken);
    }
}
