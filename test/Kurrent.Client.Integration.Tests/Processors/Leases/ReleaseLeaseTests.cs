using Kurrent.Client.Processors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Processors.Leases;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Testing;

namespace Kurrent.Client.Tests.Processors.Leases;

[Category("Leases")]
[Retry(3)]
public class ReleaseLeaseTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    const int TestTimeoutMs = 10_000;

    static StreamTemplate StreamTemplate => "$tests/leases/{0}";

    [Test]
    [Timeout(TestTimeoutMs)]
    public async ValueTask lease_released(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var acquireRequest = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(acquireRequest, cancellationToken);

        var expectedResult = ReleaseLeaseResult.Released();

        // Act
        var result = await sut.Release(
            new ReleaseLease {
                ResourceId = acquireRequest.ResourceId,
                OwnerId    = acquireRequest.OwnerId,
                Version    = acquired.Lease.Version
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    public class LeaseVersionMismatchTestCases : TestCaseGenerator<LeaseVersion> {
        protected override IEnumerable<LeaseVersion> Data() {
            yield return LeaseVersion.Unset;
            yield return LeaseVersion.From(99);
        }
    }

    [Test, LeaseVersionMismatchTestCases]
    [Timeout(TestTimeoutMs)]
    public async ValueTask lease_version_mismatch(LeaseVersion version, CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var acquireRequest = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(acquireRequest, cancellationToken);

        var expectedResult = ReleaseLeaseResult.VersionMismatch(acquired.Lease);

        // Act
        var result = await sut.Release(
            new ReleaseLease {
                ResourceId = acquireRequest.ResourceId,
                OwnerId    = acquireRequest.OwnerId,
                Version    = version
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TestTimeoutMs)]
    public async ValueTask lease_taken(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var acquireRequest = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(acquireRequest, cancellationToken);

        var expectedResult = ReleaseLeaseResult.Taken(acquired.Lease);

        // Act
        var result = await sut.Release(
            new ReleaseLease {
                ResourceId = acquireRequest.ResourceId,
                OwnerId    = Identifiers.GenerateShortId("oid"),
                Version    = acquired.Lease.Version
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TestTimeoutMs)]
    public async ValueTask lease_not_found_with_version_unset(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var expectedResult = ReleaseLeaseResult.NotFound();

        // Act
        var result = await sut.Release(
            new ReleaseLease {
                ResourceId = Identifiers.GenerateShortId("cnx"),
                OwnerId    = Identifiers.GenerateShortId("oid"),
                Version    = LeaseVersion.Unset
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TestTimeoutMs)]
    public async ValueTask lease_not_found_with_version_set(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var expectedResult = ReleaseLeaseResult.NotFound();

        // Act
        var result = await sut.Release(
            new ReleaseLease {
                ResourceId = Identifiers.GenerateShortId("cnx"),
                OwnerId    = Identifiers.GenerateShortId("oid"),
                Version    = 0
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }
}
