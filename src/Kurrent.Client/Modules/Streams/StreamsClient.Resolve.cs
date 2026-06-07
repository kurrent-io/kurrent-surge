using System.Text.RegularExpressions;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Completes a start position for a READ. Unset halves of <paramref name="position"/> are
    /// resolved against the live log: forwards resolves the first qualifying record of the
    /// target stream at-or-after the log position (the $all-forward inclusive contract);
    /// backwards the last record strictly before it (the $all-backward exclusive contract).
    /// </summary>
    /// <remarks>
    /// The position's stream half names the TARGET stream — callers must clear a foreign
    /// stream half before resolving. Returns <see cref="RecordPosition.Unset"/> when no record
    /// qualifies (known outcomes never throw); sentinels and already-complete positions pass
    /// through unchanged.
    /// </remarks>
    internal async ValueTask<RecordPosition> ResolveReadStart(
        RecordPosition position,
        ReadDirection direction = ReadDirection.Forwards,
        CancellationToken cancellationToken = default
    ) {
        if (position.IsUnset)
            throw new ArgumentException("Position must carry a log position or a stream position to resolve.", nameof(position));

        // Sentinels resolve at the wire — Earliest/Latest map natively on both targets.
        if (position.LogPosition == LogPosition.Earliest || position.LogPosition == LogPosition.Latest)
            return position;

        var hasLog    = position.LogPosition != LogPosition.Unset;
        var hasStream = position.Stream != StreamName.Unset && position.Revision != Revision.Unset;

        // Already complete — nothing to resolve. The stream half is the caller's declared target; trust it.
        if (hasLog && hasStream)
            return position;

        // Stream half only — fill the log half with one stream read at the revision. The read
        // direction follows the read: scavenged revision gaps land on the correct surviving
        // neighbour (forwards → next survivor, backwards → previous; stream reads are inclusive).
        if (hasStream) {
            var record = await ReadOneFromStream(position.Stream, position.Revision, direction, cancellationToken).ConfigureAwait(false);
            return record?.Position ?? RecordPosition.Unset;
        }

        if (!hasLog)
            throw new ArgumentException("Position must carry a log position or a complete stream position to resolve.", nameof(position));

        // Log half present — one $all read from the anchor IS the read contract: forwards is
        // inclusive (a record AT the anchor qualifies, scavenged anchors skip to the next
        // survivor), backwards is exclusive (strictly before). A target stream pins the scan
        // server-side with ^stream$; without one this is identity normalization (the record
        // the anchor points at, or the closest one in the read direction).
        var match = await ReadOneFromLog(position.LogPosition, direction, position.Stream, cancellationToken).ConfigureAwait(false);
        return match?.Position ?? RecordPosition.Unset;
    }

    /// <summary>
    /// Completes a start position for a SUBSCRIPTION — resolves the last qualifying record of
    /// the target stream at-or-before the log position, so resuming exclusively After the
    /// result is exact. Subscriptions are forward-only: no direction.
    /// </summary>
    /// <remarks>
    /// The position's stream half names the TARGET stream — callers must clear a foreign
    /// stream half before resolving. <see cref="RecordPosition.Unset"/> means no record of the
    /// stream exists at-or-before the position: the caller should subscribe from the start of
    /// the stream, NOT treat it as empty.
    /// </remarks>
    internal async ValueTask<RecordPosition> ResolveSubscriptionStart(
        RecordPosition position,
        CancellationToken cancellationToken = default
    ) {
        if (position.IsUnset)
            throw new ArgumentException("Position must carry a log position or a stream position to resolve.", nameof(position));

        // Sentinels resolve at the wire — Earliest/Latest map natively on both targets.
        if (position.LogPosition == LogPosition.Earliest || position.LogPosition == LogPosition.Latest)
            return position;

        var hasLog    = position.LogPosition != LogPosition.Unset;
        var hasStream = position.Stream != StreamName.Unset && position.Revision != Revision.Unset;

        // Already complete — nothing to resolve. The stream half is the caller's declared target; trust it.
        if (hasLog && hasStream)
            return position;

        // Stream half only — at-or-before the revision: one backwards stream read (inclusive),
        // landing on the closest surviving record at-or-below a scavenged gap.
        if (hasStream) {
            var record = await ReadOneFromStream(position.Stream, position.Revision, ReadDirection.Backwards, cancellationToken).ConfigureAwait(false);
            return record?.Position ?? RecordPosition.Unset;
        }

        if (!hasLog)
            throw new ArgumentException("Position must carry a log position or a complete stream position to resolve.", nameof(position));

        // At-or-before the anchor. The record AT the anchor qualifies, but a backwards $all
        // read is exclusive — so check the anchor first with a forwards read (inclusive: an
        // exact hit means the anchor record is the boundary), then fall back to strictly-before.
        var at = await ReadOneFromLog(position.LogPosition, ReadDirection.Forwards, position.Stream, cancellationToken).ConfigureAwait(false);

        if (at is not null && at.Position.LogPosition == position.LogPosition)
            return at.Position;

        var before = await ReadOneFromLog(position.LogPosition, ReadDirection.Backwards, position.Stream, cancellationToken).ConfigureAwait(false);
        return before?.Position ?? RecordPosition.Unset;
    }

    #region ->> Plumbing <<-

    /// <summary>
    /// Single-record $all read from an anchor. A set <paramref name="stream"/> pins the scan
    /// to that exact stream with a server-side <c>^stream$</c> regex.
    /// </summary>
    async ValueTask<Record?> ReadOneFromLogAI(LogPosition anchor, ReadDirection direction, StreamName stream, CancellationToken ct) {
        var options = new ReadRecordsOptions {
            Start        = anchor,
            Direction    = direction,
            Filter       = stream == StreamName.Unset ? RecordFilter.None : StreamRegexFilter.Parse($"^{Regex.Escape(stream)}$"),
            Limit        = 1,
            SkipDecoding = true,
            ResolveLinks = false
        };
       
        await foreach (var msg in ReadRecords(options, ct).ConfigureAwait(false)) {
            if (msg.Value is Record record)
                return record;
        }

        return null;
    }
    
    async ValueTask<Record?> ReadOneFromLog(LogPosition anchor, ReadDirection direction, StreamName stream, CancellationToken ct) {
        var options = new ReadRecordsOptions {
            Start        = anchor,
            Direction    = direction,
            Filter       = stream == StreamName.Unset ? RecordFilter.None : StreamRegexFilter.Parse($"^{Regex.Escape(stream)}$"),
            Limit        = 1,
            SkipDecoding = true,
            ResolveLinks = false
        };

        // Limit bounds the server scan (it counts EVENTS only) — but filtered reads emit
        // FilterCheckpoints ahead of the record (DEV-1770), so take the first RECORD, not
        // the first message.
        await foreach (var msg in ReadRecords(options, ct).Where(msg => msg.Value is Record).Take(1).WithCancellation(ct).ConfigureAwait(false))
            return msg.AsRecord;

        return null;
    }

    /// <summary>
    /// Single-record stream read at a revision. Missing/deleted/tombstoned streams are
    /// "no record" — resolution reports outcomes, it does not police stream state.
    /// </summary>
    async ValueTask<Record?> ReadOneFromStream(StreamName stream, Revision revision, ReadDirection direction, CancellationToken ct) {
        var options = new ReadStreamOptions {
            Start        = revision,
            Direction    = direction,
            // Limit        = 1,
            SkipDecoding = true,
            ResolveLinks = false
        };

        try {
            await foreach (var msg in ReadStream(stream, options, ct).Take(1).WithCancellation(ct).ConfigureAwait(false)) {
                if (msg.Value is Record record)
                    return record;
            }
        }
        catch (Exception ex) when (ex is StreamNotFoundException or StreamDeletedException or StreamTombstonedException) {
            // known outcomes never throw — callers needing the why call GetStreamInfo
        }

        return null;
    }

    #endregion // Plumbing
}
