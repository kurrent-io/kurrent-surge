#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Diagnostics;
using Grpc.Core;
using Kurrent.Client.Legacy;
using KurrentDB.Protocol.V1.Streams;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams.Checkpoints;
using static Kurrent.Client.Streams.StreamsClientV1Mapper;

namespace Kurrent.Client.Streams;

static class LegacyServiceClientExtensions {
    public static async ValueTask<ReadResp?> ReadFirstOrDefault(this KurrentDB.Protocol.V1.Streams.Streams.StreamsClient client, ReadReq request, CancellationToken cancellationToken = default) {
        using var session = client.Read(request, cancellationToken: cancellationToken);
        var isEmpty = !await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
        return isEmpty || session.ResponseStream.Current is null ? null : session.ResponseStream.Current;
    }
}

public partial class StreamsClient {
    public async ValueTask<StreamInfo> GetStreamInfo(StreamName stream, CancellationToken cancellationToken = default) {
        // This operation can be greatly optimized by implementing it in the server
        // and as unbelievable as it sounds, the code bellow is the simplest most effective
        // way to get the stream details for the time being.
        // Even with all the permission issues, since the operation should always work...
        // This is just code that is easy to be deleted. That's it, and that's all.

        var metadataRecord = Record.None;

        try {
            var request    = Requests.CreateReadStreamEdgeRequest(stream.Metastream, ReadDirection.Forwards);

            var response = await LegacyServiceClient
                .ReadFirstOrDefault(request, cancellationToken)
                .ConfigureAwait(false);

            // if the meta-stream is not found, then the stream itself does not exist either
            if (response?.ContentCase is ReadResp.ContentOneofCase.Event)
                metadataRecord = await response.Event
                    .MapToRecordAsync(SequenceId.None, SerializerProvider, MetadataDecoder, SchemaRegistryPolicy.NoRequirements, skipDecoding: false, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (RpcException rex) when (rex.StatusCode is StatusCode.FailedPrecondition && rex.Status.Detail.Contains("is deleted")) {
            return new StreamInfo { State = StreamState.Tombstoned };
        }
        catch (Exception ex) {
            throw ex.ToException();
        }

        var details = StreamInfo.None;

        if (metadataRecord != Record.None) {
            var metadata = (StreamMetadata)metadataRecord.Value!;
            details = new StreamInfo {
                Metadata         = metadata,
                MetadataRevision = metadataRecord.Revision,
                State            = metadata.TruncateBefore == Revision.Max ? StreamState.Deleted : StreamState.Missing
            };
        }

        // at this point if it is not deleted, we have to try to read the stream itself
        // because it might not exist yet.
        if (details.State is StreamState.Deleted)
            return details;

        // now we enrich the stream info with the latest position and revision
        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, ReadDirection.Backwards);

            var response = await LegacyServiceClient
                .ReadFirstOrDefault(request, cancellationToken)
                .ConfigureAwait(false);

            if (response is null)
                return details with { State = StreamState.Active };

            // if the stream is not found, we return the details and call it a day
            if (response.ContentCase is ReadResp.ContentOneofCase.StreamNotFound)
                return details;

            if (response.ContentCase is not ReadResp.ContentOneofCase.Event)
                throw new UnreachableException(
                    $"Unexpected content case: {response.ContentCase} " +
                    $"while reading the last record of the stream {stream}"
                );

            var record = await response.Event
                .MapToRecordAsync(SequenceId.None, SerializerProvider, MetadataDecoder, SchemaRegistryPolicy.NoRequirements, skipDecoding: true, cancellationToken)
                .ConfigureAwait(false);

            return details with {
                State = StreamState.Active,
                LastStreamPosition = record.Position.LogPosition,
                LastRevision = record.Revision,
                LastStreamAppendTime = record.Timestamp
            };
        }
        catch (Exception ex) {
            // no permissions to access the stream itself... sigh...
            // instead of returning what we have cause its ambiguous,
            // we return access denied and invalidate the operation
            throw ex.ToException();
        }
    }

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
                StreamState.Active     => new StreamRevisionConflictException(stream, revision, info.LastRevision),
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
                StreamState.Active     => new StreamRevisionConflictException(stream, revision, info.LastRevision),
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

    public async ValueTask<Revision> SetStreamMetadata(StreamName stream, StreamMetadata metadata, Revision revision, CancellationToken cancellationToken = default) {
        var result = await this.AppendRecords(
            new AppendRecord { Value = metadata },
            ConsistencyCheck.Stream(stream.Metastream).AtRevision(revision),
            cancellationToken
        ).ConfigureAwait(false);

        return result.Revisions[0].Revision;
    }

    public async ValueTask<Success> Truncate(StreamName stream, Revision truncateRevision, CancellationToken cancellationToken) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(truncateRevision, Revision.Min);

        try {
            var info = await GetStreamInfo(stream, cancellationToken).ConfigureAwait(false);

            if (info.State is StreamState.Active) {
                var actualRevision = info.LastRevision;
                var maxRevision    = actualRevision + 1; // all existing revisions

                if (truncateRevision > maxRevision)
                    throw new StreamRevisionConflictException("Truncate revision cannot be greater than the last stream revision plus one.");
            }

            var metadata = new StreamMetadata { TruncateBefore = truncateRevision };

            await SetStreamMetadata(stream, metadata, info.MetadataRevision, cancellationToken).ConfigureAwait(false);
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

    public ValueTask<Revision> ConfigureStreamDataRetention(StreamName stream, StreamDataRetentionOptions options, Revision metaRevision, CancellationToken cancellationToken) {
        var metadata = new StreamMetadata { MaxCount = options.MaxCount, MaxAge = options.MaxAge };
        return SetStreamMetadata(stream, metadata, metaRevision, cancellationToken);
    }

    // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int? maxCount, TimeSpan? maxAge, CancellationToken cancellationToken) =>
    //     ConfigureStreamDataRetention(stream, maxCount, maxAge, Revision.Unset, cancellationToken);
    //
    // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken) {
    //     ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
    //     ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCount, int.MaxValue);
    //     return SetDataRetention(stream, maxCount, null, Revision.Unset, cancellationToken);
    // }
    //
    // public ValueTask<Result<Revision, SetStreamMetadataError>> SetDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken) {
    //     ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);
    //     ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxAge, TimeSpan.MaxValue);
    //     return SetDataRetention(stream, null, maxAge, Revision.Unset, cancellationToken);
    // }
}
