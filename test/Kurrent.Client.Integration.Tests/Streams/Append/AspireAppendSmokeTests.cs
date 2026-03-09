#if NET10_0_OR_GREATER

using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
[Category("Aspire")]
public class AspireAppendSmokeTests {
    [ClassDataSource<KurrentClientAspireTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientAspireTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask appends_and_reads_back_events(CancellationToken ct) {
        // Arrange
        var simulation = HomeAutomationTestData.SimulateHomeActivity(5);

        // Act
        var appendResult = await Fixture.Client.Streams
            .AppendRecords(simulation.AppendRequest, ct);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(appendResult.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(appendResult.Revisions[0].Stream).IsEqualTo(simulation.Stream);
        await Assert.That(records).HasCount().EqualTo(5);
    }
}

#endif
