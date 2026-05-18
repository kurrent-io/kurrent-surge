#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using Grpc.Core;
using Kurrent.Client.Legacy;
using KurrentDB.Protocol.V1.Streams;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams.Checkpoints;
using Microsoft.Extensions.Logging;
using static Kurrent.Client.Streams.StreamsClientV1Mapper;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    #region ->> GetStreamInfo <<-

    public async ValueTask<StreamInfo> GetStreamInfo(StreamName stream, CancellationToken cancellationToken = default) {
        // Dudes... This is just code that is easy to be deleted. That's it, and that's all...
        //
        // It will be implemented it server-side.
        // And as unbelievable as it sounds, this implementation is the simplest most
        // effective way to get the stream details for the time being.
        // Even with all the permission issues, since the operation should always work.
        //
        // disabling read first because its too expensive in the case that the stream is truncated and need to iterate
        // all the  "stream not found" events until we get the one we want.

        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot get stream info for a metadata stream.", nameof(stream));

        var metadataRecord = Record.None;

        var readMetadataEvent = ReadLastStreamEvent(stream.Metastream);
        var readLastEvent     = ReadLastStreamEvent(stream);
        var readFirstEvent    = ReadFirstStreamEvent(stream);

        try {
            await Task.WhenAll(readMetadataEvent, readLastEvent, readFirstEvent).ConfigureAwait(false);

            var metadataEvent = await readMetadataEvent;
            if (metadataEvent is not null)
                metadataRecord = await MapToRecordAsync(metadataEvent, false).ConfigureAwait(false);

            var info = new StreamInfo { Stream = stream };

            if (metadataRecord.Value is StreamMetadata metadata) {
                info = info with {
                    State     = metadata.TruncateBefore == Revision.Max ? StreamState.Deleted : StreamState.Missing,
                    Retention = new() {
                        MaxAge         = metadata.MaxAge,
                        MaxCount       = metadata.MaxCount,
                        TruncateBefore = metadata.TruncateBefore ?? Revision.Unset
                    },
                    Acl          = metadata.Acl ?? new(),
                    Metadata     = Metadata.FromJsonDocument(metadata.CustomMetadata),
                    InfoRevision = metadataRecord.Revision,
                };
            }

            var lastEvent  = await readLastEvent;
            var firstEvent = await readFirstEvent;

            // its missing if we cannot read the edges, but we also have no metadata about it.
            if (lastEvent is null || firstEvent is null) return info;

            var lastRecord  = await MapToRecordAsync(lastEvent, skipDecoding: true).ConfigureAwait(false);
            var firstRecord = await MapToRecordAsync(firstEvent, skipDecoding: true).ConfigureAwait(false);

            return info with {
                State                 = StreamState.Active,
                FirstStreamRevision   = firstRecord.Revision,
                FirstStreamPosition   = firstRecord.Position.LogPosition,
                FirstStreamAppendedAt = firstRecord.Timestamp,
                LastStreamRevision    = lastRecord.Revision,
                LastStreamPosition    = lastRecord.Position.LogPosition,
                LastStreamAppendedAt  = lastRecord.Timestamp
            };
        }
        catch (StreamTombstonedException ex) {
            return new() { Stream = stream, State = StreamState.Tombstoned };
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }

        async Task<ReadResp.Types.ReadEvent?> ReadLastStreamEvent(StreamName targetStream, long? count = null) {
            var request = Requests.CreateReadLastStreamEventRequest(targetStream, count);

            try {
                using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

                using var logging = Logger.BeginScope(
                    "{Stream} : Looking for the last record in the stream.", targetStream);

                await foreach (var response in session.ResponseStream.ReadAllAsync(cancellationToken)) {
                    if (response.ContentCase == ReadResp.ContentOneofCase.StreamNotFound) {
                        Logger.LogDebug("Stream {Stream} not found.", targetStream);
                        return null;
                    }

                    if (response.Event is { } evt) {
                        Logger.LogDebug(
                            "Stream {Stream} : Found the last record. Revision: {Revision}, Position: {Position}.",
                            targetStream, evt.Event.StreamRevision, evt.CommitPosition);

                        return evt;
                    }

                    Logger.LogDebug("Stream {Stream} : Skipping {ResponseType} response.", targetStream, response?.ContentCase);
                }
            }
            catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                throw new StreamTombstonedException(targetStream);
            }
            catch (Exception ex) {
                throw ex.AsWellKnownException();
            }

            Logger.LogDebug("Stream {Stream} has no records", targetStream);
            return null;
        }

        async Task<ReadResp.Types.ReadEvent?> ReadFirstStreamEvent(StreamName targetStream, long? count = null) {
            var request = Requests.CreateReadFirstStreamEventRequest(targetStream, count);

            try {
                using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

                using var logging = Logger.BeginScope(
                    "{Stream} : Looking for the first non-deleted record in the stream.", targetStream);

                await foreach (var response in session.ResponseStream.ReadAllAsync(cancellationToken)) {
                    if (response?.Event is { } evt) {
                        Logger.LogDebug(
                            "Stream {Stream} : Found the first non-deleted record. Revision: {Revision}, Position: {Position}.",
                            targetStream, evt.Event.StreamRevision, evt.CommitPosition);

                        return evt;
                    }

                    Logger.LogDebug("Stream {Stream} : Skipping {ResponseType} response.", targetStream, response?.ContentCase);
                }

                return null;
            }
            catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                throw new StreamTombstonedException(targetStream);
            }
            catch (Exception ex) {
                throw ex.AsWellKnownException();
            }
        }

        ValueTask<Record> MapToRecordAsync(ReadResp.Types.ReadEvent readEvent, bool skipDecoding) {
            return readEvent.MapToRecordAsync(
                SequenceId.Unset,
                SerializerProvider,
                MetadataDecoder,
                SchemaRegistryPolicy.NoRequirements,
                skipDecoding: skipDecoding,
                cancellationToken
            );
        }
    }

    #endregion

    #region ->> Delete <<-

    public async ValueTask<LogPosition> Delete(StreamName stream, Revision revision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot delete metadata streams.", nameof(stream));

        try {
            var request = Requests.CreateDeleteRequest(stream, revision);

            var resp = await LegacyServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp.Position.MapToLogPosition();
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted, LegacyErrorCodes.WrongExpectedVersion)) {
            var info = await GetStreamInfo(stream, cancellationToken).ConfigureAwait(false);

            throw info.State switch {
                StreamState.Active     => new StreamRevisionConflictException(stream, revision, info.LastStreamRevision),
                StreamState.Deleted    => new StreamDeletedException(stream),
                StreamState.Tombstoned => new StreamTombstonedException(stream),
                StreamState.Missing    => new StreamNotFoundException(stream),
                _                      => rex
            };
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public ValueTask<LogPosition> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
        Delete(stream, Revision.Unset, cancellationToken);

    #endregion

    #region ->> Tombstone <<-

    public async ValueTask<LogPosition> Tombstone(StreamName stream, Revision revision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot tombstone metadata streams.", nameof(stream));

        try {
            var request = Requests.CreateTombstoneRequest(stream, revision);

            var resp = await LegacyServiceClient
                .TombstoneAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp?.Position?.MapToLogPosition() ?? LogPosition.Unset;
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted, LegacyErrorCodes.WrongExpectedVersion)) {
            var info = await GetStreamInfo(stream, cancellationToken).ConfigureAwait(false);

            throw info.State switch {
                StreamState.Active     => new StreamRevisionConflictException(stream, revision, info.LastStreamRevision),
                StreamState.Deleted    => new StreamDeletedException(stream),
                StreamState.Tombstoned => new StreamTombstonedException(stream),
                StreamState.Missing    => new StreamNotFoundException(stream),
                _                      => rex
            };
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public ValueTask<LogPosition> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
        Tombstone(stream, Revision.Unset, cancellationToken);

    #endregion

    #region ->> Truncate <<-

    public async ValueTask<Success> Truncate(StreamName stream, Revision truncateRevision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot truncate metadata streams.", nameof(stream));

        if (truncateRevision <= Revision.Min)
            throw new ArgumentException("Truncate revision must be greater than zero.", nameof(truncateRevision));

        var info = await GetStreamInfo(stream, cancellationToken).ConfigureAwait(false);
        if (info.State is StreamState.Active) {
            var maxRevision = info.LastStreamRevision + 1; // all existing revisions
            if (truncateRevision > maxRevision)
                throw new StreamRevisionConflictException(
                    $"Truncate revision cannot be greater than the last stream revision plus one. "
                  + $"Max revision: {maxRevision}, requested revision: {truncateRevision}");
        }

        var metadata = new StreamMetadata { TruncateBefore = truncateRevision };

        await SetStreamMetadata(stream, metadata, info.InfoRevision, cancellationToken).ConfigureAwait(false);

        return Results.Success;
    }

    #endregion

    #region ->> SetCustomMetadata <<-

    public ValueTask<Revision> SetCustomMetadata(
        StreamName stream,
        Metadata metadata,
        Revision revision,
        CancellationToken cancellationToken = default
    ) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot configure data retention for a metadata stream.", nameof(stream));

        var meta = new StreamMetadata {
            CustomMetadata = metadata.ToJsonDocument()
        };

        return SetStreamMetadata(stream, meta, revision, cancellationToken);
    }

    public async ValueTask<Revision> SetCustomMetadata(
        StreamName stream,
        Action<Metadata> update,
        CancellationToken cancellationToken = default
    ) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot configure data retention for a metadata stream.", nameof(stream));

        var info = await GetStreamInfo(stream, cancellationToken)
            .ConfigureAwait(false);

        var customMetadata = info.Metadata.CreateUnlockedCopy().With(update);
        if (customMetadata.Equals(info.Metadata)) {
            // No changes, return current revision
            return info.InfoRevision;
        }

        return await SetCustomMetadata(stream, customMetadata, info.InfoRevision, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<Revision> SetCustomMetadata(StreamName stream, Metadata metadata, CancellationToken cancellationToken = default) =>
        SetCustomMetadata(stream, x => x.Reset(metadata), cancellationToken);

    #endregion

    #region ->> ConfigureDataRetention <<-

    public ValueTask<Revision> ConfigureDataRetention(
        StreamName stream,
        StreamRetentionOptions options,
        Revision revision,
        CancellationToken cancellationToken = default
    ) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot configure data retention for a metadata stream.", nameof(stream));

        if (options.MaxAge == TimeSpan.Zero)
            throw new ArgumentException("Cannot configure data retention with zero max age.", nameof(options));

        if (options.MaxCount == 0)
            throw new ArgumentException("Cannot configure data retention with zero max count.", nameof(options));

        var meta = new StreamMetadata {
            MaxCount = options.MaxCount,
            MaxAge   = options.MaxAge
        };

        return SetStreamMetadata(stream, meta, revision, cancellationToken);
    }

    public async ValueTask<Revision> ConfigureDataRetention(
        StreamName stream,
        Action<StreamRetentionOptions> configure,
        CancellationToken cancellationToken = default
    ) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot configure data retention for a metadata stream.", nameof(stream));

        var info = await GetStreamInfo(stream, cancellationToken)
            .ConfigureAwait(false);

        StreamRetentionOptions options = info.Retention;

        configure(options);

        if (options.MaxAge == info.Retention.MaxAge && options.MaxCount == info.Retention.MaxCount) {
            // No changes, return current revision
            return info.InfoRevision;
        }

        return await ConfigureDataRetention(stream, options, info.InfoRevision, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, StreamRetention options, CancellationToken cancellationToken = default) =>
        ConfigureDataRetention(stream, x => { x.MaxCount = options.MaxCount; x.MaxAge = options.MaxAge; }, cancellationToken);

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken = default) =>
        ConfigureDataRetention(stream, x => x.MaxCount = maxCount, cancellationToken);

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken = default) =>
        ConfigureDataRetention(stream, x => x.MaxAge = maxAge, cancellationToken);

    #endregion

    async ValueTask<Revision> SetStreamMetadata(StreamName stream, StreamMetadata metadata, Revision rev, CancellationToken ct) {
        var request = AppendStreamRequest.For(stream.Metastream)
            .ExpectedState(rev)
            .AddMessage(metadata)
            .Build();

        var result = await AppendStream(request, ct).ConfigureAwait(false);

        return result.Revision;
    }
}
