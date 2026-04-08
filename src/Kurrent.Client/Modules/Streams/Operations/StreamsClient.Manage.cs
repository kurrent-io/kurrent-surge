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

        try {
            var kebas = Record.None;
        } catch (Exception e) {
            throw;
        }

        var metadataRecord = Record.None;

        // read the metastream
        try {
            if (await ReadLastStreamEvent(stream.Metastream) is { } evt)
                metadataRecord = await MapToRecordAsync(evt, false).ConfigureAwait(false);
        }
        catch (RpcException rex) when (rex.StatusCode is StatusCode.FailedPrecondition && rex.Status.Detail.Contains("is deleted")) {
            return new StreamInfo { Stream = stream, State = StreamState.Tombstoned };
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }

        var info = new StreamInfo { Stream = stream };

        if (metadataRecord.Value is StreamMetadata metadata) {
            info = info with {
                State     = metadata.TruncateBefore == Revision.Max ? StreamState.Deleted : StreamState.Missing,
                Retention = new StreamRetention {
                    MaxAge   = metadata.MaxAge,
                    MaxCount = metadata.MaxCount
                },
                //CacheControl = metadata.CacheControl,
                Acl          = metadata.Acl ?? StreamAcl.Unset,
                Metadata     = Metadata.FromJsonDocument(metadata.CustomMetadata),
                InfoRevision = metadataRecord.Revision,
            };
        }

        // no point reading the stream edges because unfortunately,
        // the protocol will return stream not found.
        if (info.State is StreamState.Deleted)
            return info;

        // reads last and first events from the stream
        try {
            var readLastEvent  = ReadLastStreamEvent(stream);
            var readFirstEvent = ReadFirstStreamEvent(stream);

            await Task.WhenAll(readLastEvent, readFirstEvent).ConfigureAwait(false);

            var lastEvent  = await readLastEvent;
            var firstEvent = await readFirstEvent;

            // just a stupid fallback in case the stream is not found — return current details (Missing state)
            if (lastEvent is null || firstEvent is null)
                return info;

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
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }


        async Task<ReadResp.Types.ReadEvent?> ReadLastStreamEvent(StreamName targetStream) {
            var request = Requests.CreateReadLastStreamEventRequest(targetStream);
            using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);
            await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
            return session.ResponseStream.Current?.Event;
        }

        async Task<ReadResp.Types.ReadEvent?> ReadFirstStreamEvent(StreamName targetStream, long? count = null) {
            var request = Requests.CreateReadFirstStreamEventRequest(targetStream, count);

            using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

            using var logging = Logger.BeginScope("{Stream} : Looking for the first non-deleted record in the stream.", targetStream);

            await foreach (var response in session.ResponseStream.ReadAllAsync(cancellationToken)) {
                if (response?.Event is { } evt ) {
                    Logger.LogDebug(
                        "Found the first non-deleted event. Revision: {Revision}, Position: {Position}.",
                        evt.Event.StreamRevision, evt.CommitPosition);

                    return evt;
                }

                Logger.LogDebug("Skipping {ResponseType} response.", response?.ContentCase);
            }

            return null;
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
        try {
            var request = Requests.CreateDeleteRequest(stream, revision);

            var resp = await LegacyServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp.Position.MapToLogPosition();
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted) || rex.IsLegacyError(LegacyErrorCodes.WrongExpectedVersion)) {
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
            throw ex.AsWellKnownException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                StreamRevisionConflictException.From);
        }
    }

    public ValueTask<LogPosition> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
        Delete(stream, Revision.Unset, cancellationToken);

    #endregion

    #region ->> Tombstone <<-

    public async ValueTask<LogPosition> Tombstone(StreamName stream, Revision revision, CancellationToken cancellationToken = default) {
        try {
            var request = Requests.CreateTombstoneRequest(stream, revision);

            var resp = await LegacyServiceClient
                .TombstoneAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp?.Position?.MapToLogPosition() ?? LogPosition.Unset;
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted) || rex.IsLegacyError(LegacyErrorCodes.WrongExpectedVersion)) {
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
            throw ex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                StreamRevisionConflictException.From);
        }
    }

    public ValueTask<LogPosition> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
        Tombstone(stream, Revision.Unset, cancellationToken);

    #endregion

    #region ->> Truncate <<-

    public async ValueTask<Success> Truncate(StreamName stream, Revision truncateRevision, CancellationToken cancellationToken) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(truncateRevision, Revision.Min);

        try {
            var info = await GetStreamInfo(stream, cancellationToken).ConfigureAwait(false);

            if (info.State is StreamState.Active) {
                var actualRevision = info.LastStreamRevision;
                var maxRevision    = actualRevision + 1; // all existing revisions

                if (truncateRevision > maxRevision)
                    throw new StreamRevisionConflictException("Truncate revision cannot be greater than the last stream revision plus one.");
            }

            var metadata = new StreamMetadata { TruncateBefore = truncateRevision };

            await SetStreamMetadata(stream, metadata, info.InfoRevision, cancellationToken).ConfigureAwait(false);
            return Results.Success;
        }
        catch (Exception ex) {
            throw ex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                StreamRevisionConflictException.From);
        }
    }

    #endregion

    #region ->> SetCustomMetadata <<-

    public ValueTask<Revision> SetCustomMetadata(
        StreamName stream,
        Metadata metadata,
        Revision revision,
        CancellationToken cancellationToken = default
    ) {
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
        SetCustomMetadata(stream, metadata, Revision.Unset, cancellationToken);

    #endregion

    #region ->> ConfigureDataRetention <<-

    public async ValueTask<Revision> ConfigureDataRetention(
        StreamName stream,
        Func<StreamRetention, StreamRetention> configure,
        CancellationToken cancellationToken
    ) {
        var info = await GetStreamInfo(stream, cancellationToken)
            .ConfigureAwait(false);

        var options = configure(info.Retention with { });
        if (options.Equals(info.Retention)) {
            // No changes, return current revision
            return info.InfoRevision;
        }

        return await ConfigureDataRetention(stream, options, info.InfoRevision, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<Revision> ConfigureDataRetention(
        StreamName stream,
        StreamRetention options,
        Revision revision,
        CancellationToken cancellationToken
    ) {
        var meta = new StreamMetadata {
            MaxCount = options.MaxCount,
            MaxAge   = options.MaxAge
        };

        return SetStreamMetadata(stream, meta, revision, cancellationToken);
    }

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, StreamRetention options, CancellationToken cancellationToken) =>
        ConfigureDataRetention(stream, options, Revision.Unset, cancellationToken);

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken) =>
        ConfigureDataRetention(stream, x => x with { MaxCount = maxCount }, cancellationToken);

    public ValueTask<Revision> ConfigureDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken) =>
        ConfigureDataRetention(stream, x => x with { MaxAge = maxAge }, cancellationToken);

    #endregion

    async ValueTask<Revision> SetStreamMetadata(StreamName stream, StreamMetadata metadata, Revision rev, CancellationToken ct) {
        var builder = AppendStreamRequest
            .For(stream.Metastream)
            .WithRecord(rec => rec.WithValue(metadata));

        if (rev != Revision.Unset)
            builder.ExpectingState(rev);

        var result = await AppendStream(builder.Build(), ct).ConfigureAwait(false);

        return result.Revision;
    }
}
