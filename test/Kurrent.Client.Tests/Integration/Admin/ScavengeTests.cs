using Kurrent.Client.Admin;
using Kurrent.Client.Streams;
using Sdk.TestKit.Fixtures;
using Serilog;

namespace Kurrent.Client.Tests.Integration.Admin;

[Category("Admin")]
[Category("Integration")]
[Timeout(10_000)]
// [Skip("Scavenge tests are destructive and impact the shared test database, "
//     + "and are not currently run in CI. They should be run manually when needed.")]
public class ScavengeTests {
    //TODO SS: Use Aspire instead of fluent docker to allow creating isolated databases for these tests, and remove the Skip attribute.

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_scavenge_events(CancellationToken ct) {
        // // Arrange — ensure at least one scavenge has run
        // var scavenge = await Fixture.Client.Admin.StartScavenge(cancellationToken: ct);
        //
        // try {
        //     await Fixture.Client.Admin.StopScavenge(scavenge.ScavengeId, ct);
        // }
        // catch {
        //     // May have already completed
        // }

        // Act — read the $scavenges system stream
        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords("$scavenges", Revision.Min, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records.Count).IsGreaterThanOrEqualTo(1);

        foreach (var record in records) {
            await Assert.That(record.Stream).IsEqualTo("$scavenges");
            await Assert.That(record.Schema.SchemaName).IsNotEmpty();

            Log.Information(
                "Scavenge event {SchemaName} revision {Revision} @ {Position} : {@Event}",
                record.Schema.SchemaName, record.Revision, record.Position, record.Value
            );
        }
    }

    [Test]
    public async ValueTask start_scavenge_returns_started_or_in_progress(CancellationToken ct) {
        // Act
        var result = await Fixture.Client.Admin.StartScavenge(cancellationToken: ct);

        // Assert
        await Assert.That(result.ScavengeId).IsNotEmpty();
        await Assert.That(result.Status is ScavengeStatus.Started or ScavengeStatus.InProgress).IsTrue();

        // Cleanup — stop if still running
        try {
            await Fixture.Client.Admin.StopScavenge(result.ScavengeId, ct);
        }
        catch {
            // May have already completed
        }
    }

    // StopScavenge is not tested: the gRPC call hangs when the scavenge has already
    // completed, and on a small test database scavenge finishes almost instantly.

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
}
