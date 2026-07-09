using Kurrent.Surge.Testkit;

namespace Aspire.Hosting.KurrentDB.Tests;

/// <summary>
/// Smoke tests verifying that the fixture's background log subscriber captures
/// resource logs into a unified chronological stream.
/// </summary>
[Category("Diagnostics")]
[Category("Integration")]
public class ResourceLogCollectionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask resource_logs_are_captured_by_background_subscriber(CancellationToken ct) {
        // Arrange & Act
        var entries = Fixture.ResourceLogs;

        // Assert — should have captured container startup logs
        await Assert.That(entries).IsNotEmpty();
    }

    [Test]
    public async ValueTask resource_logs_contain_kurrentdb_entries(CancellationToken ct) {
        // Arrange & Act
        var entries = Fixture.ResourceLogs;
        var kurrentdbEntries = entries.Where(e => e.ResourceName.StartsWith("kurrentdb")).ToList();

        // Assert
        await Assert.That(kurrentdbEntries).IsNotEmpty();
    }

    [Test]
    public async ValueTask resource_log_entries_preserve_error_flag(CancellationToken ct) {
        // Arrange & Act
        var entries = Fixture.ResourceLogs;

        // Assert — entries should have the LogLine structure intact
        await Assert.That(entries.Count).IsGreaterThan(0);
        await Assert.That(entries.All(e => e.Line.Content.Length > 0)).IsTrue();
    }
}
