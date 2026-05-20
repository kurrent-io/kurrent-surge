using Kurrent.Sdk.Testkit;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Diagnostics;

/// <summary>
/// Smoke tests verifying that the fixture's background log subscriber captures
/// resource logs and surfaces them on test failure via ITestEndEventReceiver.
/// </summary>
[Category("Diagnostics")]
[Category("Integration")]
public class ResourceLogCollectionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask resource_logs_are_captured_by_background_subscriber(CancellationToken ct) {
        // Arrange & Act — the background subscriber has been buffering since before StartAsync
        var logs = Fixture.CollectLogs();

        Fixture.Logger.LogDebug("Collected {Length} chars of resource logs", logs.Length);

        // Assert — KurrentDB container should have emitted startup logs
        await Assert.That(logs).IsNotEmpty();
        await Assert.That(logs).Contains("kurrentdb");
    }

    [Test]
    public async ValueTask resource_logs_contain_server_startup_output(CancellationToken ct) {
        // Arrange & Act
        var logs = Fixture.CollectLogs();

        // Assert — should have substantial content from KurrentDB startup
        await Assert.That(logs.Length).IsGreaterThan(100);
    }
}
