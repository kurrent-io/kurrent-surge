using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// Exercises <c>ResolveReadStart</c> / <c>ResolveSubscriptionStart</c> (internal, not yet
/// integrated into routing) against a live server. The interleaved seed creates the foreign
/// mid-range anchors that the boundary-search fallback exists for:
/// <code>
///   log order:  A0 A1 A2 | B0..B4 | A3 A4
///   anchor B2 sits INSIDE stream A's range but belongs to B.
/// </code>
/// See research: .claude/context/docs/research/2026-06-07-start-position-resolution/.
/// </summary>
[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class ResolveStartTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // The Resolve* hooks are internal white-box members of the concrete client — they are not
    // part of the IStreamsClient contract, so these tests reach them through the concrete type.
    StreamsClient Streams => (StreamsClient)Fixture.Client.Streams;

    #region ->> Pass-through <<-

    [Test]
    public async ValueTask returns_complete_position_unchanged(CancellationToken ct) {
        // Arrange
        var seed = await SeedInterleaved(ct);

        var complete = seed.A[1].Position; // all three halves present

        // Act
        var resolved = await Streams.ResolveReadStart(complete, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(complete);
    }

    [Test]
    public async ValueTask returns_sentinels_unchanged(CancellationToken ct) {
        // Act
        var earliest = await Streams.ResolveReadStart(RecordPosition.Earliest, ct: ct);
        var latest   = await Streams.ResolveSubscriptionStart(RecordPosition.Latest, ct);

        // Assert
        await Assert.That(earliest).IsEqualTo(RecordPosition.Earliest);
        await Assert.That(latest).IsEqualTo(RecordPosition.Latest);
    }

    [Test]
    public async ValueTask throws_when_position_is_unset(CancellationToken ct) {
        // Act & Assert — a fully unset position is a caller bug, not a resolvable outcome
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await Streams.ResolveReadStart(RecordPosition.Unset, ct: ct));
    }

    #endregion // Pass-through

    #region ->> Fill log half (stream + revision known) <<-

    [Test]
    public async ValueTask fills_log_position_from_stream_revision(CancellationToken ct) {
        // Arrange
        var seed = await SeedInterleaved(ct);

        var expected = seed.A[2].Position;
        var input    = RecordPosition.From(LogPosition.Unset, expected.Stream, expected.Revision);

        // Act
        Fixture.Logger.LogInformation("Resolving (stream, r{Revision}) without log half — expect {Expected}", expected.Revision.Value, expected);
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask returns_unset_when_stream_is_missing(CancellationToken ct) {
        // Arrange — a stream name that was never written
        var input = RecordPosition.From(LogPosition.Unset, StreamName.From($"missing-{Guid.NewGuid():N}"), Revision.Min);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert — known outcome, never throws
        await Assert.That(resolved).IsEqualTo(RecordPosition.Unset);
    }

    #endregion // Fill log half

    #region ->> Read boundary (AtOrAfter / Before) <<-

    [Test]
    public async ValueTask read_start_resolves_own_record_at_log_position(CancellationToken ct) {
        // Arrange — anchor is a record OF the target stream: fast path, boundary = itself
        var seed = await SeedInterleaved(ct);

        var expected = seed.A[1].Position;
        var input    = RecordPosition.From(expected.LogPosition, expected.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask read_start_resolves_next_record_after_foreign_position(CancellationToken ct) {
        // Arrange — anchor B2 is foreign and INSIDE A's range; first A-record at-or-after it is A3
        var seed = await SeedInterleaved(ct);

        var anchor   = seed.B[2].Position.LogPosition;
        var expected = seed.A[3].Position;
        var input    = RecordPosition.From(anchor, seed.Stream, Revision.Unset);

        // Act
        Fixture.Logger.LogInformation("Resolving read start at foreign mid-range anchor {Anchor} — expect {Expected}", anchor.Value, expected);
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask read_start_backwards_resolves_previous_record_before_own_position(CancellationToken ct) {
        // Arrange — backwards is the $all EXCLUSIVE flavor: own anchor A1 resolves to A0
        var seed = await SeedInterleaved(ct);

        var anchor   = seed.A[1].Position;
        var expected = seed.A[0].Position;
        var input    = RecordPosition.From(anchor.LogPosition, anchor.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ReadDirection.Backwards, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask read_start_backwards_resolves_last_record_before_foreign_position(CancellationToken ct) {
        // Arrange — anchor B2 foreign mid-range; last A-record strictly before it is A2
        var seed = await SeedInterleaved(ct);

        var anchor   = seed.B[2].Position.LogPosition;
        var expected = seed.A[2].Position;
        var input    = RecordPosition.From(anchor, seed.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ReadDirection.Backwards, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask read_start_backwards_returns_unset_at_first_record(CancellationToken ct) {
        // Arrange — Before(A0) has no previous record in A
        var seed = await SeedInterleaved(ct);

        var input = RecordPosition.From(seed.A[0].Position.LogPosition, seed.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ReadDirection.Backwards, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(RecordPosition.Unset);
    }

    [Test]
    public async ValueTask read_start_returns_unset_when_position_past_stream_end(CancellationToken ct) {
        // Arrange — anchor past A's last record: nothing of A qualifies at-or-after
        var seed = await SeedInterleaved(ct);

        var after = await Fixture.SeedTestStream(1, ct); // lands after A4 in the log

        var input = RecordPosition.From(after.Position, seed.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(RecordPosition.Unset);
    }

    #endregion // Read boundary

    #region ->> Subscription boundary (AtOrBefore) <<-

    [Test]
    public async ValueTask subscription_start_resolves_own_record_at_log_position(CancellationToken ct) {
        // Arrange — own anchor satisfies ≤: boundary = itself, After(it) is the exact resume
        var seed = await SeedInterleaved(ct);

        var expected = seed.A[1].Position;
        var input    = RecordPosition.From(expected.LogPosition, expected.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveSubscriptionStart(input, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask subscription_start_resolves_last_record_before_foreign_position(CancellationToken ct) {
        // Arrange — checkpoint at foreign B2: last processed A-record is A2, resume After(A2)
        var seed = await SeedInterleaved(ct);

        var anchor   = seed.B[2].Position.LogPosition;
        var expected = seed.A[2].Position;
        var input    = RecordPosition.From(anchor, seed.Stream, Revision.Unset);

        // Act
        Fixture.Logger.LogInformation("Resolving subscription start at foreign anchor {Anchor} — expect {Expected}", anchor.Value, expected);
        var resolved = await Streams.ResolveSubscriptionStart(input, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask subscription_start_returns_unset_when_position_precedes_stream(CancellationToken ct) {
        // Arrange — anchor before A's first record: nothing processed yet → caller subscribes from start
        var preSeed = await Fixture.LatestRecordPosition(ct);
        var seed    = await SeedInterleaved(ct);

        var input = RecordPosition.From(preSeed.LogPosition, seed.Stream, Revision.Unset);

        // Act
        var resolved = await Streams.ResolveSubscriptionStart(input, ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(RecordPosition.Unset);
    }

    #endregion // Subscription boundary

    #region ->> Identity normalization (no stream half) <<-

    [Test]
    public async ValueTask normalizes_identity_of_record_at_log_position(CancellationToken ct) {
        // Arrange — log-only input: complete it with the record it points at (Drone hydration case)
        var seed = await SeedInterleaved(ct);

        var expected = seed.B[2].Position;
        var input    = RecordPosition.From(expected.LogPosition);

        // Act
        var resolved = await Streams.ResolveReadStart(input, ct: ct);

        // Assert
        await Assert.That(resolved).IsEqualTo(expected);
    }

    #endregion // Identity normalization

    #region ->> Harness <<-

    record InterleavedSeed(StreamName Stream, List<Record> A, List<Record> B);

    /// <summary>
    /// Seeds the interleaved layout A0..A2 | B0..B4 | A3 A4 so B's records are foreign
    /// anchors INSIDE A's log range — the case the fallback boundary search exists for.
    /// </summary>
    async ValueTask<InterleavedSeed> SeedInterleaved(CancellationToken ct) {
        var a = await Fixture.SeedTestStream(3, ct);
        var b = await Fixture.SeedTestStream(5, ct);

        var more = await Fixture.TestData.GenerateSet(2, ct);
        await Fixture.Client.Streams.AppendStream(a.Stream, more.Records, ct);

        var aRecords = await ReadAllStreamRecords(a.Stream, ct);
        var bRecords = await ReadAllStreamRecords(b.Stream, ct);

        Fixture.Logger.LogInformation(
            "Seeded interleaved: A = {Stream} [{A}], foreign B = [{B}]",
            a.Stream,
            string.Join(" ", aRecords.Select(r => $"r{r.Position.Revision.Value}@{r.Position.LogPosition.Value}")),
            string.Join(" ", bRecords.Select(r => $"r{r.Position.Revision.Value}@{r.Position.LogPosition.Value}")));

        return new(a.Stream, aRecords, bRecords);
    }

    async ValueTask<List<Record>> ReadAllStreamRecords(StreamName stream, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStream(stream, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return records;
    }

    #endregion // Harness
}
