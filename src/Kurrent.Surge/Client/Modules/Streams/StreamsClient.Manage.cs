#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using Grpc.Core;
using Kurrent.Surge.Schema;
using KurrentDB.Protocol.V1.Streams;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams.Checkpoints;
using static Kurrent.Surge.Client.Streams.StreamsClientMapping;

namespace Kurrent.Surge.Client.Streams;

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

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

        if (stream.IsMetadataStream)
            throw new ArgumentException("Cannot get stream info for a metadata stream.", nameof(stream));

        var metadataRecord = Record.None;

        var readMetadataEvent = ReadStreamEdgeEvent(stream.Metastream, ReadDirection.Backwards);
        var readLastEvent     = ReadStreamEdgeEvent(stream, ReadDirection.Backwards);
        var readFirstEvent    = ReadStreamEdgeEvent(stream, ReadDirection.Forwards);

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

        // GetStreamInfo is the stream-state authority: ReadCore calls it to turn a V1 StreamNotFound
        // message into the right typed exception, so these edge reads must NOT route through ReadCore /
        // ReadStreamEdge — that would recurse. They read raw via the legacy client and report state for
        // GetStreamInfo to interpret: null = no record (stream not found or empty), StreamTombstonedException
        // = hard-deleted. count: 1 because only the edge record is needed.
        async Task<ReadResp.Types.ReadEvent?> ReadStreamEdgeEvent(StreamName targetStream, ReadDirection direction) {
            var request = Requests.CreateReadStreamEdgeRequest(targetStream, direction, count: 1);

            try {
                using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

                using var logging = Logger.BeginScope(
                    "{Stream} : Looking for the {Direction} record in the stream.", targetStream, direction);

                await foreach (var response in session.ResponseStream.ReadAllAsync(cancellationToken)) {
                    if (response.ContentCase == ReadResp.ContentOneofCase.StreamNotFound) {
                        Logger.LogDebug("Stream {Stream} not found.", targetStream);
                        return null;
                    }

                    if (response.Event is { } evt) {
                        Logger.LogDebug(
                            "Stream {Stream} : Found the {Direction} record. Revision: {Revision}, Position: {Position}.",
                            targetStream, direction, evt.Event.StreamRevision, evt.CommitPosition);

                        return evt;
                    }

                    Logger.LogDebug("Stream {Stream} : Skipping {ResponseType} response.", targetStream, response.ContentCase);
                }
            }
            catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                throw new StreamTombstonedException(targetStream);
            }
            catch (Exception ex) {
                throw ex.AsWellKnownException();
            }

            Logger.LogDebug("Stream {Stream} : has no records.", targetStream);
            return null;
        }

        ValueTask<Record> MapToRecordAsync(ReadResp.Types.ReadEvent readEvent, bool skipDecoding) =>
            readEvent.MapToRecordAsync(SequenceId.Unset, CreateMappingContext(cancellationToken), skipDecoding);
    }

    #endregion

    #region ->> Delete <<-

    public async ValueTask<LogPosition> Delete(StreamName stream, Revision revision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

    #endregion

    #region ->> Tombstone <<-

    public async ValueTask<LogPosition> Tombstone(StreamName stream, Revision revision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

    #endregion

    #region ->> Truncate <<-

    public async ValueTask<Success> Truncate(StreamName stream, Revision truncateRevision, CancellationToken cancellationToken = default) {
        if (stream.IsUnset)
            throw new ArgumentException("Stream name cannot be empty.", nameof(stream));

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

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

    #endregion

    async ValueTask<Revision> SetStreamMetadata(StreamName stream, StreamMetadata metadata, Revision rev, CancellationToken ct) {
        var record = AppendRecord.Create(metadata, r => {
            r.Stream     = stream.Metastream;
            r.DataFormat = SchemaDataFormat.Json;
        });

        // Revision.Unset means "no optimistic-concurrency expectation". The removed builder dropped an
        // Unset (or Any) guard; the new core only short-circuits on Any, so branch here to avoid sending
        // an invalid revision check on the metastream.
        var result = rev == Revision.Unset
            ? await this.AppendStream(stream.Metastream, [record], ct).ConfigureAwait(false)
            : await this.AppendStream(stream.Metastream, [record], rev, ct).ConfigureAwait(false);

        return result.Revision;
    }
}
