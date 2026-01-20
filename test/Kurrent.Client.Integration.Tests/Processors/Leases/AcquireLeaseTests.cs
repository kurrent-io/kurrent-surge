using Kurrent.Client.Processors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Processors.Leases;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Testing;
using static Kurrent.Processors.Leases.AcquireOrRenewLeaseResult;

namespace Kurrent.Client.Tests.Processors.Leases;

[Category("Leases")]
// [Retry(3)]
public class AcquireLeaseTests {
    const int TimeoutMs = 30_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    static StreamTemplate StreamTemplate => "$tests/leases/{0}";

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_acquired_when_no_lease_exists(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var expectedResult = Acquired(
            new VersionedLease {
                ResourceId = request.ResourceId,
                AcquiredBy = request.OwnerId,
                AcquiredAt = clock.GetCurrentInstant(),
                Duration   = request.Duration,
                Version    = 0
            }
        );

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        // Act
        var result = await sut.AcquireOrRenew(request, cancellationToken);

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_acquired_when_lease_from_another_client_is_expired(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(request, cancellationToken);

        clock.AdvanceSeconds(120);

        var expectedResult = Acquired(
            acquired.Lease with {
                AcquiredBy = "node-2",
                AcquiredAt = clock.GetCurrentInstant(),
                Duration = request.Duration,
                Version = acquired.Lease.Version.Next()
            }
        );

        // Act
        var result = await sut.AcquireOrRenew(
            request with {
                OwnerId = "node-2",
                Version = LeaseVersion.Unset
            },
            cancellationToken
        );

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_renewed(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(request, cancellationToken);

        clock.AdvanceSeconds(60);

        var expectedResult = Renewed(
            new VersionedLease {
                ResourceId = request.ResourceId,
                AcquiredBy = request.OwnerId,
                AcquiredAt = clock.GetCurrentInstant(),
                Duration   = request.Duration,
                Version    = acquired.Lease.Version.Next()
            }
        );

        // Act
        var result = await sut.AcquireOrRenew(request with { Version = acquired.Lease.Version }, cancellationToken);

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_version_mismatch(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(request, cancellationToken);

        var expectedResult = VersionMismatch(acquired.Lease);

        // Act
        var result = await sut.AcquireOrRenew(request with { Version = 99 }, cancellationToken);

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_version_mismatch_when_version_is_unset(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(request, cancellationToken);

        var expectedResult = VersionMismatch(acquired.Lease);

        // Act
        var result = await sut.AcquireOrRenew(request with { Version = LeaseVersion.Unset }, cancellationToken);

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }

    [Test]
    [Timeout(TimeoutMs)]
    public async ValueTask lease_taken(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var sut = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("cnx"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(30)
        };

        var acquired = await sut.AcquireOrRenew(request with { OwnerId = Identifiers.GenerateShortId("oid") }, cancellationToken);

        var expectedResult = Taken(acquired.Lease);

        // Act
        var result = await sut.AcquireOrRenew(request, cancellationToken);

        // Assert
        result.ShouldBeEquivalentTo(expectedResult);
    }
}
