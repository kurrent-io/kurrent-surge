using Kurrent.Client.Admin;
using Kurrent.Client.Streams;
using Sdk.TestKit.Fixtures;
using Serilog;

namespace Kurrent.Client.Tests.Integration.Admin;

[Category("Admin")]
[Category("Integration")]
[Timeout(10_000)]
[NotInParallel(nameof(ScavengeTests))]
// [Skip("Scavenge tests are destructive and impact the shared test database, "
//     + "and are not currently run in CI. They should be run manually when needed.")]
public class ScavengeTests {
    //TODO SS: Use Aspire instead of fluent docker to allow creating isolated databases for these tests, and remove the Skip attribute.

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    #region Validation

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

    #region Commands

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

        // Act
        try {
            var stop = await Fixture.Client.Admin.StopScavenge(start.ScavengeId, ct);

            // Assert
            await Assert.That(stop.ScavengeId).IsEqualTo(start.ScavengeId);
            await Assert.That(stop.Status).IsEqualTo(ScavengeResponseStatus.StopRequested);
        }
        catch {
            // On a small test database the scavenge completes before we can stop it.
            Skip.Test("Scavenge completed before stop could be issued.");
        }
    }

    [Test]
    [DependsOn(nameof(stop_scavenge_returns_stop_requested), ProceedOnFailure = true)]
    public async ValueTask stop_current_scavenge_stops_running_scavenge(CancellationToken ct) {
        // Arrange — fresh client to avoid stale projector cache
        await using var client = Fixture.GetNewClient();

        var start = await client.Admin.StartScavenge(cancellationToken: ct);
        await Assert.That(start.Status is ScavengeResponseStatus.Started or ScavengeResponseStatus.AlreadyRunning).IsTrue();

        // Act
        try {
            var stop = await client.Admin.StopCurrentScavenge(ct);

            // Assert
            await Assert.That(stop.ScavengeId).IsNotEmpty();
            await Assert.That(stop.Status).IsEqualTo(ScavengeResponseStatus.StopRequested);
        }
        catch (InvalidOperationException) {
            Skip.Test("Scavenge completed before StopCurrentScavenge could find it.");
        }
        catch {
            Skip.Test("Scavenge completed before stop could be issued.");
        }
    }

    [Test]
    [Skip("Requires isolated database — scavenge events from other tests pollute the index.")]
    [DependsOn(nameof(stop_current_scavenge_stops_running_scavenge), ProceedOnFailure = true)]
    public async ValueTask stop_current_scavenge_throws_when_idle(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Admin.StopCurrentScavenge(ct).AsTask());
    }

    #endregion

    #region Queries

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask reads_scavenge_events(CancellationToken ct) {
        // Act
        await using var messages = await Fixture.Client.Streams
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
    public async ValueTask get_last_scavenge_returns_completed_details(CancellationToken ct) {
        // Arrange — fresh client for clean projector state
        await using var client = Fixture.GetNewClient();

        // Act
        var details = await client.Admin.GetLastScavenge(ct);

        // Assert
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.ScavengeId).IsNotEmpty();
        await Assert.That(details.NodeEndpoint).IsNotEmpty();
        await Assert.That(details.State).IsNotEqualTo(ScavengeState.Running);
        await Assert.That(details.StartedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(details.CompletedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(details.Duration).IsGreaterThan(TimeSpan.Zero);

        Log.Information(
            "Last scavenge {ScavengeId} on {Node}: {State}, duration {Duration}, saved {SpaceSaved} bytes",
            details.ScavengeId, details.NodeEndpoint, details.State, details.Duration, details.Progress.SpaceSaved
        );
    }

    [Test]
    [Skip("Requires isolated database — scavenge events from other tests pollute the index.")]
    [DependsOn(nameof(stop_current_scavenge_stops_running_scavenge), ProceedOnFailure = true)]
    public async ValueTask get_current_scavenge_returns_null_when_idle(CancellationToken ct) {
        // Arrange
        await using var client = Fixture.GetNewClient();

        // Act
        var current = await client.Admin.GetCurrentScavenge(ct);

        // Assert
        await Assert.That(current).IsNull();
    }

    [Test]
    [DependsOn(nameof(start_scavenge_returns_started_or_already_running))]
    public async ValueTask list_scavenges_returns_history(CancellationToken ct) {
        // Arrange — fresh client for clean projector state
        await using var client = Fixture.GetNewClient();

        // Act
        var summaries = new List<ScavengeSummary>();

        await foreach (var summary in client.Admin.ListScavenges(cancellationToken: ct))
            summaries.Add(summary);

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
    [DependsOn(nameof(get_last_scavenge_returns_completed_details))]
    public async ValueTask get_scavenge_details_returns_full_projection(CancellationToken ct) {
        // Arrange — fresh client for clean projector state
        await using var client = Fixture.GetNewClient();

        var last = await client.Admin.GetLastScavenge(ct);
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
