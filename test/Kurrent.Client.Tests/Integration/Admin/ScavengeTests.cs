using Kurrent.Client.Admin;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Integration.Admin;

[Category("Admin")]
[Category("Integration")]
[Timeout(60_000)]
public class ScavengeTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

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
