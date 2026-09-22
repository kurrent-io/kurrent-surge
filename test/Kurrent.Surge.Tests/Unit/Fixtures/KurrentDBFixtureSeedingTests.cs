using Bogus;
using Kurrent.Testkit.Aspire;
using Testing.ArcadeRacer;

namespace Kurrent.Surge.Tests.Fixtures;

[Category("Testkit")]
public class KurrentDBFixtureSeedingTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public class SeedFailures : TestCaseDataGenerator<Exception> {
        protected override IEnumerable<TestCase<Exception>> Data() {
            yield return new(new OperationCanceledException(), DisplayName: "cancelled underneath");
            yield return new(new InvalidOperationException(), DisplayName: "failed");
        }
    }

    [Test]
    [SeedFailures]
    public async ValueTask seeding_many_streams_fails_as_setup_failure_when_a_seed_does_not_complete(Exception seedFailure, CancellationToken ct) {
        // Arrange
        await using var fixture = new KurrentDBFixture {
            Faker    = Faker,
            TestData = new FailingTestDataSource(seedFailure)
        };

        var numberOfStreams = Faker.Random.Int(2, 11);

        // Act
        var exception = await Assert.That(async () => await fixture.SeedManyTestStreams(numberOfStreams, ct))
            .ThrowsExactly<AspireFixtureException>();

        // Assert
        var cause = exception?.InnerException;

        await Assert.That(cause).IsSameReferenceAs(seedFailure);
    }

    sealed class FailingTestDataSource(Exception failure) : ITestDataSource {
        public string Name => nameof(FailingTestDataSource);

        public ValueTask<TestStream> GenerateSet(int numberOfEvents, CancellationToken cancellationToken) =>
            ValueTask.FromException<TestStream>(failure);
    }
}
