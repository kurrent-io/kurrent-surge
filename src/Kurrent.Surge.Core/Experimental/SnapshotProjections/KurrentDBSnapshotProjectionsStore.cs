using System.Globalization;

using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.SnapshotProjections;

/// <summary>
/// Stores snapshots as ordinary records on a stream, one record per version.
/// </summary>
/// <remarks>
/// Failures surface as the streams client's own typed exceptions — an expected-state mismatch on save
/// arrives as the corresponding stream-state exception, carrying the stream and the expected versus
/// actual state.
/// </remarks>
sealed class KurrentDBSnapshotProjectionsStore(IStreamsClient streams) : ISnapshotProjectionsStore {
    /// <summary>
    /// Metadata key carrying the event time of the message that produced the snapshot. Distinct from
    /// <see cref="Record.Timestamp"/>, which is when the snapshot record itself was appended.
    /// </summary>
    /// <remarks>
    /// Held as an invariant round-trip ISO-8601 string, never as a <see cref="DateTime"/> or
    /// <see cref="DateTimeOffset"/> value. Metadata values that are not strings, numbers, or booleans
    /// reach the wire through <c>source.ToString()</c>, which renders a date in the host's current
    /// culture and drops both the offset and the sub-second digits — the value would come back a
    /// culture-formatted string that no longer parses to the same instant.
    /// </remarks>
    public const string SnapshotTimestampKey = "snapshot.timestamp";

    public async ValueTask<(TSnapshot Snapshot, RecordPosition Position, DateTimeOffset Timestamp)> LoadSnapshot<TSnapshot>(
        StreamName snapshotStream,
        CancellationToken cancellationToken = default
    ) where TSnapshot : class, new() {
        Record record;

        try {
            record = await streams
                .LastRecord(snapshotStream, skipDecoding: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StreamNotFoundException) {
            // Never projected: there is no snapshot to load, and the caller guards its first save on
            // the stream's absence.
            return (new TSnapshot(), RecordPosition.Unset, DateTimeOffset.MinValue);
        }

        // A stream that exists but has no readable records (every record expired or truncated away)
        // yields Record.None, which falls out of this check along with undecodable payloads.
        if (record.Value is not TSnapshot snapshot)
            return (new(), record.Position, DateTimeOffset.MinValue);

        // Read as a DateTime, not as the string it was written as: the read path re-types an ISO-8601
        // metadata string into a DateTime, zoned to the host (Kind=Local) when the string carries an
        // offset — which it always does here. ToUniversalTime therefore recovers the stored instant.
        // A snapshot written before this key existed reads as MinValue, which loses every staleness
        // comparison rather than winning one by accident.
        var timestamp = record.Metadata.TryGetValue<DateTime>(SnapshotTimestampKey, out var value)
            ? new(value.ToUniversalTime())
            : DateTimeOffset.MinValue;

        return (snapshot, record.Position, timestamp);
    }

    public async ValueTask SaveSnapshot<TSnapshot>(
        TSnapshot snapshot,
        StreamName snapshotStream,
        ExpectedStreamState expectedState,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default
    ) where TSnapshot : class, new() {
        var record = AppendRecord.Create(snapshot, x => {
            x.Stream = snapshotStream;
            x.Metadata[SnapshotTimestampKey] = timestamp.ToString("O", CultureInfo.InvariantCulture);
        });

        await streams
            .AppendStream(record, expectedState, cancellationToken)
            .ConfigureAwait(false);
    }
}
