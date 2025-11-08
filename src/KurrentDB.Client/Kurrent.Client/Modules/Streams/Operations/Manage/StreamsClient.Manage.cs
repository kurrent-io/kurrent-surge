#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Diagnostics;
using Grpc.Core;
using Kurrent.Client.Legacy;
using KurrentDB.Client;
using KurrentDB.Protocol.V1.Streams;
using Kurrent.Client.Exceptions;
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
    public async ValueTask<Result<StreamInfo, GetStreamInfoError>> GetStreamInfo(StreamName stream, CancellationToken cancellationToken = default) {
        // This operation can be greatly optimized by implementing it in the server
        // and as unbelievable as it sounds, the code bellow is the simplest most effective
        // way to get the stream details for the time being.
        // Even with all the permission issues, since the operation should always work...
        // This is just code that is easy to be deleted. That's it, and that's all.

        var metadataRecord = Record.None;

        try {
            var metaStream = SystemStreams.MetastreamOf(stream);
            var request    = Requests.CreateReadStreamEdgeRequest(metaStream, ReadDirection.Forwards);

            var response = await LegacyServiceClient
                .ReadFirstOrDefault(request, cancellationToken)
                .ConfigureAwait(false);

            // if the meta-stream is not found, then the stream itself does not exist either
            if (response?.ContentCase is ReadResp.ContentOneofCase.Event)
                metadataRecord = await response.Event
                    .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding: false, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (RpcException rex) when (rex.StatusCode is StatusCode.FailedPrecondition && rex.Status.Detail.Contains("is deleted")) {
            return new StreamInfo { State = StreamState.Tombstoned };
        }
        catch (Exception ex) {
            return GetStreamInfoError.FromEx(ex);
        }

        var details = StreamInfo.None;

        if (metadataRecord != Record.None) {
            var metadata = (StreamMetadata)metadataRecord.Value!;
            details = new StreamInfo {
                Metadata         = metadata,
                MetadataRevision = metadataRecord.StreamRevision,
                State            = metadata.TruncateBefore == StreamRevision.Max ? StreamState.Deleted : StreamState.Missing
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
                .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding: true, cancellationToken)
                .ConfigureAwait(false);

            return details with {
                State                = StreamState.Active,
                LastStreamPosition   = record.Position,
                LastStreamRevision   = record.StreamRevision,
                LastStreamAppendTime = record.Timestamp
            };
        }
        catch (Exception ex) {
            // no permissions to access the stream itself... sigh...
            // instead of returning what we have cause its ambiguous,
            // we return access denied and invalidate the operation
            return GetStreamInfoError.FromEx(ex);
        }
    }

    public async ValueTask<Result<LogPosition, DeleteStreamError>> Delete(StreamName stream, StreamRevision revision, CancellationToken cancellationToken = default) {
        try {
            var request = Requests.CreateDeleteRequest(stream, revision);

            var resp = await LegacyServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp.Position.MapToLogPosition();
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted) || rex.IsLegacyError(LegacyErrorCodes.WrongExpectedVersion)) {
            dynamic error = await GetStreamInfo(stream, cancellationToken).MatchAsync(
                ok => ok.State switch {
                    StreamState.Active     => new StreamRevisionConflictException(nameof(Delete), stream, revision, ok.LastStreamRevision),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Missing    => new StreamNotFoundException(stream)
                },
                ko => ko.AsException
            );

            return Result<LogPosition, DeleteStreamError>.Failure(error);
        }
        catch (Exception ex) {
            return DeleteStreamError.FromEx(ex);
        }
    }

    public async ValueTask<Result<LogPosition, TombstoneStreamError>> Tombstone(StreamName stream, StreamRevision revision, CancellationToken cancellationToken = default) {
        try {
            var request = Requests.CreateTombstoneRequest(stream, revision);

            var resp = await LegacyServiceClient
                .TombstoneAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return resp?.Position?.MapToLogPosition() ?? LogPosition.Unset;
        }
        catch (RpcException rex) when (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted) || rex.IsLegacyError(LegacyErrorCodes.WrongExpectedVersion)) {
            dynamic error = await GetStreamInfo(stream, cancellationToken).MatchAsync(
                ok => ok.State switch {
                    StreamState.Active     => new StreamRevisionConflictException(nameof(Tombstone), stream, revision, ok.LastStreamRevision),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Missing    => new StreamNotFoundException(stream)
                },
                ko => ko.AsException
            );

            return Result<LogPosition, TombstoneStreamError>.Failure(error);
        }
        catch (Exception ex) {
            return TombstoneStreamError.FromEx(ex);
        }
    }

    public ValueTask<Result<StreamRevision, SetStreamMetadataError>> SetStreamMetadata(StreamName stream, StreamMetadata metadata, StreamRevision expectedRevision, CancellationToken cancellationToken = default) {
        var metaStream = SystemStreams.MetastreamOf(stream);

        var req = AppendStreamRequest.New()
            .ForStream(metaStream)
            .ExpectingRevision(expectedRevision)
            .WithRecord(metadata)
            .Build();

        return this.AppendStream(req, cancellationToken)
            .MapErrorAsync(ko => ko.ForwardError<SetStreamMetadataError>())
            .MapAsync(ok => ok.StreamRevision);
    }

    public async ValueTask<Result<Success, TruncateStreamError>> Truncate(StreamName stream, StreamRevision truncateRevision, CancellationToken cancellationToken) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(truncateRevision, StreamRevision.Min);

        try {
            var info = await GetStreamInfo(stream, cancellationToken).UnwrapAsync(); // we can let it flow ;)

            if (info.State is StreamState.Active) {
                var actualRevision = info.LastStreamRevision;
                var maxRevision    = actualRevision + 1; // all existing revisions

                if (truncateRevision > maxRevision)
                    return Result.Failure<Success, TruncateStreamError>(new StreamRevisionConflictException("Truncate revision cannot be greater than the last stream revision plus one."));
            }

            var metadata = new StreamMetadata { TruncateBefore = truncateRevision };

            return await SetStreamMetadata(stream, metadata, info.MetadataRevision, cancellationToken)
                .MapErrorAsync(ko => ko.ForwardError<TruncateStreamError>())
                .MapAsync(Results.Success);
        }
        catch (Exception ex) {
            return TruncateStreamError.FromEx(ex);
        }
    }


    public ValueTask<Result<StreamRevision, SetStreamMetadataError>> ConfigureStreamDataRetention(StreamName stream, StreamDataRetentionOptions options, StreamRevision metaStreamRevision, CancellationToken cancellationToken) {
        var metadata = new StreamMetadata { MaxCount = options.MaxCount, MaxAge = options.MaxAge };
        return SetStreamMetadata(stream, metadata, metaStreamRevision, cancellationToken);
    }

    // public ValueTask<Result<StreamRevision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int? maxCount, TimeSpan? maxAge, CancellationToken cancellationToken) =>
    //     ConfigureStreamDataRetention(stream, maxCount, maxAge, StreamRevision.Unset, cancellationToken);
    //
    // public ValueTask<Result<StreamRevision, SetStreamMetadataError>> SetDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken) {
    //     ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
    //     ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCount, int.MaxValue);
    //     return SetDataRetention(stream, maxCount, null, StreamRevision.Unset, cancellationToken);
    // }
    //
    // public ValueTask<Result<StreamRevision, SetStreamMetadataError>> SetDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken) {
    //     ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);
    //     ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxAge, TimeSpan.MaxValue);
    //     return SetDataRetention(stream, null, maxAge, StreamRevision.Unset, cancellationToken);
    // }
}
