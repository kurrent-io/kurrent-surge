namespace Kurrent.Surge.Tests.Fixtures;

[Category("Testkit")]
[Category("Integration")]
[Timeout(10_000)]
public class KurrentDBFixtureTestStreamsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask surfaces_cancellation_when_the_callers_token_is_cancelled_mid_set(CancellationToken ct) {
        // Arrange
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var expectedSeeded = Fixture.Faker.Random.Int(1, 2);
        var expectedToken  = cancellator.Token;
        var seeded         = 0;

        // Act
        var exception = await Assert.That(async () => {
            await foreach (var _ in Fixture.TestStreams(cancellator.Token))
                if (++seeded == expectedSeeded)
                    await cancellator.CancelAsync();
        }).ThrowsExactly<OperationCanceledException>();

        // Assert
        var cancelledToken = exception?.CancellationToken;

        await Assert.That(seeded).IsEqualTo(expectedSeeded);
        await Assert.That(cancelledToken).IsEqualTo(expectedToken);
    }
}
