using Kurrent.Client.Admin;
using Kurrent.Client.Streams;
using Kurrent.Testkit.Aspire;
using Serilog;

namespace Kurrent.Client.Tests.Integration.Admin;

[Category("Admin")]
[Category("Integration")]
[Timeout(15_000)]
[NotInParallel(nameof(ScavengeTests))]
[Skip("Scavenge tests are destructive and impact the shared test database, "
    + "and are not currently run in CI. They should be run manually when needed.")]
public class ScavengeTests {
    //TODO SS: Use Aspire instead of fluent docker to allow creating isolated databases for these tests, and remove the Skip attribute.

    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    #region ->> Validation <<-

    [Test]
    public async ValueTask start_scavenge_validates_thread_count(CancellationToken ct) {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Fixture.Client.Admin.StartScavenge(threadCount: 0, cancellationToken: ct).AsTask());
    }

    [Test]
    public async ValueTask start_scavenge_validates_start_from_chunk(CancellationToken ct) {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Fixture.Client.Admin.StartScavenge(startFromChunk: -1, cancellationToken: ct).AsTask());
    }

    [Test]
    public async ValueTask stop_scavenge_validates_scavenge_id(CancellationToken ct) {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => Fixture.Client.Admin.StopScavenge("", ct).AsTask());
    }

    [Test]
    public async ValueTask get_scavenge_details_validates_scavenge_id(CancellationToken ct) {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => Fixture.Client.Admin.GetScavengeDetails("", ct).AsTask());
    }

    #endregion

    #region ->> Commands <<-

    [Test]
    public async ValueTask start_scavenge_returns_started_or_already_running(CancellationToken ct) {
        // Act
        var response = await Fixture.Client.Admin.StartScavenge(cancellationToken: ct);

        // Assert
        await Assert.That(response.ScavengeId).IsNotEmpty();
        await Assert.That(response.Status is ScavengeResponseStatus.Started or ScavengeResponseStatus.AlreadyRunning).IsTrue();

        // Cleanup
        try { await Fixture.Client.Admin.StopScavenge(response.ScavengeId, ct); }
        catch { /* may have already completed */ }
    }

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask start_scavenge_convenience_overload_returns_response(CancellationToken ct) {
        // Act
        var response = await Fixture.Client.Admin.StartScavenge(ct);

        // Assert
        await Assert.That(response.ScavengeId).IsNotEmpty();
        await Assert.That(response.Status is ScavengeResponseStatus.Started or ScavengeResponseStatus.AlreadyRunning).IsTrue();

        // Cleanup
        try { await Fixture.Client.Admin.StopScavenge(response.ScavengeId, ct); }
        catch { /* may have already completed */ }
    }

    [Test]
    [DependsOn(nameof(start_scavenge_convenience_overload_returns_response))]
    public async ValueTask stop_scavenge_returns_stop_requested(CancellationToken ct) {
        // Arrange
        var start = await Fixture.Client.Admin.StartScavenge(cancellationToken: ct);

        // Act — returns StopRequested whether the scavenge is still running or already completed
        var stop = await Fixture.Client.Admin.StopScavenge(start.ScavengeId, ct);

        // Assert
        await Assert.That(stop.ScavengeId).IsEqualTo(start.ScavengeId);
        await Assert.That(stop.Status).IsEqualTo(ScavengeResponseStatus.StopRequested);
    }

    [Test]
    [DependsOn(nameof(stop_scavenge_returns_stop_requested))]
    public async ValueTask stop_current_scavenge_returns_stop_requested(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        var start = await client.Admin.StartScavenge(cancellationToken: ct);
        await Assert.That(start.Status is ScavengeResponseStatus.Started or ScavengeResponseStatus.AlreadyRunning).IsTrue();

        // Act — StopCurrentScavenge finds the running scavenge via GetLatestScavenge.
        // If it completed before the stop call, StopScavenge handles the NotFound gracefully.
        try {
            var stop = await client.Admin.StopCurrentScavenge(ct);

            // Assert
            await Assert.That(stop.ScavengeId).IsNotEmpty();
            await Assert.That(stop.Status).IsEqualTo(ScavengeResponseStatus.StopRequested);
        }
        catch (InvalidOperationException) {
            // The scavenge completed before GetLatestScavenge could see it as running.
            // This is expected on a small test database where scavenges finish instantly.
        }
    }

    [Test]
    [Skip("Requires isolated database — scavenge events from other tests pollute the index.")]
    [DependsOn(nameof(stop_current_scavenge_returns_stop_requested), ProceedOnFailure = true)]
    public async ValueTask stop_current_scavenge_throws_when_idle(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act & Assert — GetLatestScavenge returns a non-running scavenge, so StopCurrentScavenge throws
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Admin.StopCurrentScavenge(ct).AsTask());
    }

    #endregion

    #region ->> Queries <<-

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask reads_scavenge_events(CancellationToken ct) {
        // Act
        var messages = Fixture.Client.Streams
            .ReadStreamRecords("$scavenges", Revision.Min, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

        foreach (var record in records) {
            await Assert.That(record.Schema.SchemaName.Value).IsNotEmpty();

            Log.Information(
                "Scavenge event {SchemaName} revision {Revision} @ {Position} : {@Event}",
                record.Schema.SchemaName, record.Revision, record.Position, record.Value
            );
        }
    }

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask get_latest_scavenge_returns_summary(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act
        var summary = await client.Admin.GetLatestScavenge(ct);

        // Assert
        await Assert.That(summary).IsNotNull();
        await Assert.That(summary!.ScavengeId).IsNotEmpty();
        await Assert.That(summary.NodeEndpoint).IsNotEmpty();
        await Assert.That(summary.StartedAt).IsNotEqualTo(default(DateTime));

        Log.Information(
            "Latest scavenge {ScavengeId} on {Node}: {State}, started {StartedAt}, saved {SpaceSaved} bytes",
            summary.ScavengeId, summary.NodeEndpoint, summary.State, summary.StartedAt, summary.SpaceSaved
        );
    }

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask list_scavenges_returns_history(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act
        var summaries = await client.Admin.ListScavenges(ct).ToListAsync(ct);

        // Assert
        await Assert.That(summaries.Count).IsGreaterThanOrEqualTo(1);

        foreach (var summary in summaries) {
            await Assert.That(summary.ScavengeId).IsNotEmpty();
            await Assert.That(summary.NodeEndpoint).IsNotEmpty();
            await Assert.That(summary.StartedAt).IsNotEqualTo(default(DateTime));

            Log.Information(
                "Scavenge {ScavengeId} on {Node}: {State}, started {StartedAt}",
                summary.ScavengeId, summary.NodeEndpoint, summary.State, summary.StartedAt
            );
        }
    }

    [Test]
    public async ValueTask list_scavenges_take_limits_results(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act — Take(3) should stop reading after 3 summaries and dispose the stream
        var summaries = await client.Admin.ListScavenges(ct).Take(3).ToListAsync(ct);

        // Assert
        await Assert.That(summaries.Count).IsGreaterThan(0).And.IsLessThanOrEqualTo(3);

        foreach (var summary in summaries) {
            await Assert.That(summary.ScavengeId).IsNotEmpty();

            Log.Information(
                "Scavenge (take 3) {ScavengeId}: {State}, started {StartedAt}",
                summary.ScavengeId, summary.State, summary.StartedAt
            );
        }
    }

    [Test]
    public async ValueTask reads_scavenge_index_backwards(CancellationToken ct) {
        // Act — backwards read of $scavenges with Take
        var messages = Fixture.Client.Streams
            .ReadStream(
                ReadStreamRequest.New.ForStream("$scavenges")
                    .FromEnd()
                    .Build(),
                ct
            );

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .Take(3)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsGreaterThan(0);

        foreach (var record in records)
            Log.Information("Backwards: {Schema} rev={Revision}", record.Schema.SchemaName, record.Revision);
    }

    [Test]
    public async ValueTask list_scavenges_take_one_returns_latest(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act — Take(1) is effectively what GetLatestScavenge does
        var summaries = await client.Admin.ListScavenges(ct).Take(1).ToListAsync(ct);

        // Assert
        await Assert.That(summaries.Count).IsEqualTo(1);
        await Assert.That(summaries[0].ScavengeId).IsNotEmpty();
        await Assert.That(summaries[0].StartedAt).IsNotEqualTo(default(DateTime));

        Log.Information(
            "Latest via Take(1): {ScavengeId} {State}, started {StartedAt}",
            summaries[0].ScavengeId, summaries[0].State, summaries[0].StartedAt
        );
    }

    [Test]
    public async ValueTask list_scavenges_are_ordered_newest_first(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act
        var summaries = await client.Admin.ListScavenges(ct).Take(5).ToListAsync(ct);

        // Assert — each StartedAt should be >= the next (newest first)
        for (var i = 0; i < summaries.Count - 1; i++) {
            await Assert.That(summaries[i].StartedAt)
                .IsGreaterThanOrEqualTo(summaries[i + 1].StartedAt);
        }
    }

    [Test]
    public async ValueTask list_scavenges_caches_completed_entries(CancellationToken ct) {
        // Arrange — same client instance so the cache is shared across calls
        await using var client = Fixture.GetNewClient();

        // Act — first call populates cache, second call should serve from cache
        var first  = await client.Admin.ListScavenges(ct).Take(3).ToListAsync(ct);
        var second = await client.Admin.ListScavenges(ct).Take(3).ToListAsync(ct);

        // Assert — same results
        await Assert.That(second.Count).IsEqualTo(first.Count);

        for (var i = 0; i < first.Count; i++)
            await Assert.That(second[i].ScavengeId).IsEqualTo(first[i].ScavengeId);
    }

    [Test]
    [DependsOn(nameof(get_latest_scavenge_returns_summary))]
    public async ValueTask get_scavenge_details_returns_full_projection(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        var last = await client.Admin.GetLatestScavenge(ct);
        await Assert.That(last).IsNotNull();

        // Act
        var details = await client.Admin.GetScavengeDetails(last!.ScavengeId, ct);

        // Assert
        await Assert.That(details.ScavengeId).IsEqualTo(last.ScavengeId);
        await Assert.That(details.NodeEndpoint).IsNotEmpty();
        await Assert.That(details.State).IsNotEqualTo(ScavengeState.Running);
        await Assert.That(details.StartedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(details.CompletedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(details.Configuration).IsNotNull();
        await Assert.That(details.Progress).IsNotNull();

        Log.Information(
            "Scavenge {ScavengeId} details: {State}, chunks scavenged={ChunksScavenged}, "
            + "chunks skipped={ChunksSkipped}, merged={Merged}, index tables={IndexTables}, "
            + "space saved={SpaceSaved} bytes",
            details.ScavengeId, details.State,
            details.Progress.ChunksScavenged, details.Progress.ChunksSkipped,
            details.Progress.ChunksMerged, details.Progress.IndexTablesScavenged,
            details.Progress.SpaceSaved
        );
    }

    #endregion
}
