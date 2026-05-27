using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Variant;
using KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

// Checkpoint is a barrier (coordination token). Server-emitted cases carry
// the position(s) the server has confirmed; processor-synthesized cases
// (ManualCheckpoint, ShutdownCheckpoint, etc.) leave it empty.
// Streaming extends this hierarchy without touching client.
public abstract record Checkpoint(CheckpointId Id, DateTimeOffset Timestamp) {
    public IReadOnlyList<RecordPosition> Positions { get; init; } = [];
}

public sealed record IntervalCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

public sealed record CaughtUpCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

/// <summary>
/// Represents a unique session identifier assigned by the server when a subscription is established.
/// </summary>
[PublicAPI]
public readonly record struct SessionId {
    /// <summary>
    /// Represents an unset session (e.g., drone never connected).
    /// </summary>
    public static readonly SessionId Unset = new(Guid.Empty);

    SessionId(Guid value) => Value = value;

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Indicates whether this session ID is unset.
    /// </summary>
    public bool IsUnset => this == Unset;

    /// <summary>
    /// Creates a <see cref="SessionId"/> from a GUID value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/>.</exception>
    public static SessionId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("SessionId cannot be empty.", nameof(value))
            : new(value);

    public static implicit operator Guid(SessionId _) => _.Value;
    public static implicit operator SessionId(Guid _) => From(_);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Unique identifier for a checkpoint.
/// </summary>
/// <remarks>
/// Checkpoint IDs are coordination tokens, not positions.
/// Drones use these IDs to identify which checkpoint they're responding to.
/// </remarks>
[PublicAPI]
public readonly record struct CheckpointId(string Value) {
    public static readonly CheckpointId Unset = new("");

    /// <summary>Creates a new unique checkpoint ID.</summary>
    public static CheckpointId New() => new(Guid.NewGuid().ToString());

    public static CheckpointId From(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CheckpointId(value);
    }

    public static CheckpointId From(ulong value) =>
        new(value.ToString());

    public static CheckpointId From(long value) =>
        new(value.ToString());

    /// <summary>True if this checkpoint ID is empty or unset.</summary>
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    public static implicit operator string(CheckpointId id) => id.Value;
}

public readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly SubscribeMessage None;
}

public readonly record struct Connected(SessionId SessionId);

public partial class StreamsClient {
    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        RecordFilter filter,
        RecordPosition start,
        bool skipDecoding,
        bool resolveLinks = true,
        uint maxSearchWindow = StreamsClientMapping.DefaultMaxSearchWindow, // we must not expose this and make it a max value internally so it never gets used.
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var request = StreamsClientMapping.Requests.CreateSubscriptionRequest(filter, start, resolveLinks, maxSearchWindow);
        
        AsyncServerStreamingCall<ReadResp> grpcSession;
        SessionId                          sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = LegacyServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"EVERYTHING IS BROKEN! First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}");

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                throw new LogPositionNotFoundException(position, rex);
            }

            throw rex.AsWellKnownException();
        }

        yield return new Connected(sessionId);

        SequenceGenerator sequencer = new();

        // C# forbids yield inside try/catch, so we use a flag to break out of the
        // loop on cancellation and handle cleanup in the finally block.
        try {
            while (await grpcSession.ResponseStream.MoveNextSafe(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint:
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.CaughtUp:
                        yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.Event:
                        var ctx = CreateMappingContext(ct);
                        var record = await resp.Event
                            .MapToRecordAsync(sequencer.FetchNext(), ctx, skipDecoding)
                            .ConfigureAwait(false);

                        if (filter.IsMatch(record))
                            yield return record;

                    break;
                }
            }
        }
        finally {
            grpcSession.Dispose();
        }
    }
}
