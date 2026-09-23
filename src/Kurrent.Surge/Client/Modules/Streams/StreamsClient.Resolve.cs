using System.Text.RegularExpressions;
using Kurrent.Surge.Client.Exceptions;
using static Kurrent.Surge.Client.Streams.StreamsClientMapping;

namespace Kurrent.Surge.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Single entry for a read start. Routes by the <paramref name="filter"/> to a TARGET — a
    /// stream-name filter targets that stream, anything else targets the log — and completes the
    /// start into the target's currency, delegating the I/O to <see cref="ResolveReadStart"/>.
    /// Returns <see cref="RecordPosition.Unset"/> when the read is empty by definition (from the
    /// end forwards) or nothing qualifies; throws when the start cannot address the target at all.
    /// A log read backwards from a stream anchor also returns the anchor record, which the read
    /// must deliver first: the <c>$all</c> backwards read excludes its start position.
    /// </summary>
    internal async ValueTask<(RecordPosition Start, Record? Anchor)> ResolveStartPosition(
        RecordFilter filter,
        RecordPosition start,
        ReadDirection direction = ReadDirection.Forwards,
        CancellationToken cancellationToken = default
    ) {
        if (filter.UnwrapFilter() is StreamNameFilter streamFilter) {
            var target = streamFilter.TargetStream;

            // Sentinels speak both currencies: pin them to the target so the wire maps them
            // natively (Earliest → first revision, Latest → end of stream).
            start =
                start.LogPosition == LogPosition.Earliest ? RecordPosition.From(LogPosition.Earliest, target, Revision.Min)
                : start.LogPosition == LogPosition.Latest ? RecordPosition.From(LogPosition.Latest, target, Revision.Max)
                : start;

            // No position given (no log half, no revision) → the stream's natural edge for the
            // direction: first record forwards (Min), last record backwards (Max).
            if (start.LogPosition == LogPosition.Unset && start.Revision == Revision.Unset)
                start = RecordPosition.From(LogPosition.Unset, target,
                    direction == ReadDirection.Forwards ? Revision.Min : Revision.Max);

            // "From the end, forwards" reads nothing — the stream wire cannot even express it
            // (End + Forwards is rejected server-side). An empty read, not an error.
            if (start.Revision == Revision.Max && direction == ReadDirection.Forwards)
                return (RecordPosition.Unset, null);

            // Native: the stream half already names THIS target and carries a revision — trust it.
            if (start.Stream == target && start.Revision != Revision.Unset)
                return (start, null);

            // Otherwise the log half is the address; a foreign stream half is never trusted.
            // Resolve the log position to the target's boundary record.
            if (start.LogPosition == LogPosition.Unset)
                throw new ArgumentException(
                    "Start position cannot address the target stream: it carries neither a " +
                    "matching stream revision nor a log position to resolve one from.", nameof(start));

            var resolved = await ResolveReadStart(
                    RecordPosition.From(start.LogPosition, target, Revision.Unset), direction, cancellationToken)
                .ConfigureAwait(false);

            return (resolved, null);
        }

        // Log target. A log half is the address — use it directly (sentinels included).
        if (start.LogPosition != LogPosition.Unset)
            return (start, null);

        // A named stream is a trusted anchor: translate it into a log position (stream→log) — the
        // direction a foreign-filtered ReadStream re-routes through $all. A missing revision means
        // "no position given" → the stream's natural edge for the direction (Min forwards, Max
        // backwards).
        if (start.Stream != StreamName.Unset) {
            var revision = start.Revision != Revision.Unset
                ? start.Revision
                : direction == ReadDirection.Forwards ? Revision.Min : Revision.Max;

            var anchor = await ReadOneFromStream(start.Stream, revision, direction, cancellationToken).ConfigureAwait(false);

            if (anchor is null)
                return (RecordPosition.Unset, null);

            // The $all backwards read excludes the record at its start position, so the anchor is
            // handed back for the read to deliver. The secondary-index route is inclusive and
            // delivers the anchor itself.
            var deliverAnchor = direction == ReadDirection.Backwards && filter.UnwrapFilter() is not IndexFilter;

            return (anchor.Position, deliverAnchor ? anchor : null);
        }

        // No log half and no stream anchor → no position given: the log's natural edge for the
        // direction (beginning forwards, end backwards).
        if (start.IsUnset)
            return (direction == ReadDirection.Forwards ? RecordPosition.Earliest : RecordPosition.Latest, null);

        throw new ArgumentException(
            "Log-targeted read start carries neither a log position nor an addressable stream.", nameof(start));
    }

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
        CancellationToken ct = default
    ) {
        if (position.IsUnset || position.LogPosition == LogPosition.Unset && position.Stream == StreamName.Unset && position.Revision == Revision.Unset )
            throw new ArgumentException("Position must carry a log position or a stream position to resolve.", nameof(position));

        // Sentinels resolve at the wire — Earliest/Latest map natively on both targets. Revision
        // sentinels (Min/Max) carry no log half and take the stream branch below.
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
            var record = await ReadOneFromStream(position.Stream, position.Revision, direction, ct).ConfigureAwait(false);
            return record?.CheckpointPosition ?? RecordPosition.Unset;
        }

        if (!hasLog)
            throw new ArgumentException("Position must carry a log position or a complete stream position to resolve.", nameof(position));

        // Log half present — one $all read from the anchor IS the read contract: forwards is
        // inclusive (a record AT the anchor qualifies, scavenged anchors skip to the next
        // survivor), backwards is exclusive (strictly before). A target stream pins the scan
        // server-side with ^stream$; without one this is identity normalization (the record
        // the anchor points at, or the closest one in the read direction).
        var match = await ReadOneFromLog(position.LogPosition, direction, position.Stream, ct).ConfigureAwait(false);
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
            return record?.CheckpointPosition ?? RecordPosition.Unset;
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
    
    async ValueTask<Record?> ReadOneFromLog(LogPosition anchor, ReadDirection direction, StreamName stream, CancellationToken ct) {
        var options = new ReadRecordsOptions {
            Start        = anchor,
            Direction    = direction,
            Filter       = stream == StreamName.Unset ? RecordFilter.None : StreamRegexFilter.Parse($"^{Regex.Escape(stream)}$"),
            Limit        = 1,
            SkipDecoding = true
        };

        // Limit bounds the server scan (count tallies events only); take the first RECORD —
        // a read can still carry non-record messages such as the CaughtUp marker.
        await foreach (var msg in ReadRecords(options, ct).Where(msg => msg.Value is Record).Take(1).WithCancellation(ct).ConfigureAwait(false))
            return msg.AsRecord;

        return null;
    }

    /// <summary>
    /// Single-record stream read at a revision, links unresolved: the record is the row physically
    /// in the stream, as a log read delivers it. Missing/deleted/tombstoned streams are
    /// "no record" — resolution reports outcomes, it does not police stream state.
    /// </summary>
    async ValueTask<Record?> ReadOneFromStream(StreamName stream, Revision revision, ReadDirection direction, CancellationToken ct) {
        // "From the end, forwards" holds no record, and the stream wire rejects End + Forwards.
        if (revision == Revision.Max && direction == ReadDirection.Forwards)
            return null;

        var request = Requests.CreateReadStreamRequest(stream, revision, direction, 1);

        try {
            await foreach (var msg in ReadCore(request, skipDecoding: true, throwOnUnresolved: false, RecordFilter.None, DefaultMaxSearchWindow, ct).ConfigureAwait(false)) {
                if (msg.Value is Record record)
                    return record;
            }
        }
        catch (Exception ex) when (ex is StreamNotFoundException or StreamDeletedException or StreamTombstonedException) {
            // known outcomes never throw — callers needing the why call GetStreamInfo
        }

        return null;
    }
}
