using Kurrent.Client.Processors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Processors.Leases;
using Kurrent.Processors.Leases;
using Kurrent.Processors.Locks;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Testing;

namespace Kurrent.Client.Tests.Processors.Locks;

[Category("Leases")]
[Retry(3)]
public class ServiceLockerTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    const int DefaultTestTimeoutMs = 10_000;

    static StreamTemplate StreamTemplate => "$tests/leases/{0}";

    [Test, Timeout(30_000)]
    public async ValueTask locks_service_when_no_lease_exists(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var leaseManager = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = Identifiers.GenerateShortId("svc"),
                OwnerId       = Identifiers.GenerateShortId("oid"),
                LeaseDuration = Duration.FromSeconds(240)
            },
            leaseManager
        );

        // Act
        var locked = await sut.EnsureLocked(cancellationToken);

        // Assert
        locked.IsCancellationRequested.ShouldBeFalse();
    }

    public class LocksServiceWhenExistingLeaseIsExpiredTestCases : TestCaseGenerator<string, string> {
        protected override IEnumerable<(string, string)> Data() {
            var ownerId = Identifiers.GenerateShortId("oid");

            yield return (ownerId, ownerId);
            yield return (ownerId, Identifiers.GenerateShortId("oid"));
        }
    }

    [Test, LocksServiceWhenExistingLeaseIsExpiredTestCases]
    [Timeout(DefaultTestTimeoutMs)]
    public async ValueTask locks_service_when_existing_lease_is_expired(string ownerId, string newOwnerId, CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var leaseManager = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("svc"),
            OwnerId    = ownerId,
            Duration   = Duration.FromSeconds(1)
        };

        await leaseManager.AcquireOrRenew(request, cancellationToken);

        clock.AdvanceDays(1);

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = request.ResourceId,
                OwnerId       = newOwnerId,
                LeaseDuration = Duration.FromSeconds(240),
                Retry = new() {
                    Timeout = Duration.FromMinutes(10),
                    Delay   = Duration.FromMinutes(1)
                }
            },
            leaseManager
        );

        // Act
        var locked = await sut.EnsureLocked(cancellationToken);

        // Assert
        locked.IsCancellationRequested.ShouldBeFalse(); //The test should not have been cancelled automatically.
    }

    [Test]
    [Timeout(DefaultTestTimeoutMs)]
    public async ValueTask fails_to_lock_service_on_lease_acquisition_timeout(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var leaseManager = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var request = new AcquireOrRenewLease {
            ResourceId = Identifiers.GenerateShortId("svc"),
            OwnerId    = Identifiers.GenerateShortId("oid"),
            Duration   = Duration.FromSeconds(60)
        };

        var result = await leaseManager.AcquireOrRenew(request, cancellationToken);

        var options = new ServiceLockerOptions {
            ResourceId    = request.ResourceId,
            OwnerId       = Identifiers.GenerateShortId("oid"),
            LeaseDuration = Duration.FromSeconds(60),
            Retry = new() {
                Timeout = Duration.FromSeconds(3),
                Delay   = Duration.FromSeconds(1)
            },
            ReleaseTimeout = Duration.FromSeconds(3)
        };

        var sut = new ServiceLocker(options, leaseManager);

        // Act & Assert
        await sut.EnsureLocked(cancellationToken)
            .ShouldThrowAsync<TimeoutException>($"The acquisition of the lease should have timed out in {options.Retry.Timeout}.");
    }

    [Test]
    [Timeout(DefaultTestTimeoutMs)]
    public async ValueTask unlocks_service(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var leaseManager = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = Identifiers.GenerateShortId("svc"),
                OwnerId       = Identifiers.GenerateShortId("oid"),
                LeaseDuration = Duration.FromSeconds(240)
            },
            leaseManager
        );

        var locked = await sut.EnsureLocked(cancellationToken);

        // Act & Assert
        locked.IsCancellationRequested.ShouldBeFalse();

        await sut.DisposeAsync();

        locked.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Timeout(DefaultTestTimeoutMs)]
    public async ValueTask try_unlock_signals_lease_lost_on_exception(CancellationToken cancellationToken) {
        // Arrange
        var fakeLeaseManager = A.Fake<ILeaseManager>();

        A.CallTo(() => fakeLeaseManager.AcquireOrRenew(A<AcquireOrRenewLease>._, A<AcquireOrRenewLeaseRetryOptions>._, A<CancellationToken>._))
            .Returns(
                AcquireOrRenewLeaseResult.Acquired(
                    A.Fake<VersionedLease>() with {
                        Duration = Duration.FromSeconds(240)
                    }
                )
            );

        A.CallTo(() => fakeLeaseManager.Release(A<ReleaseLease>._, A<CancellationToken>._))
            .ThrowsAsync(new Exception("Boom!"));

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = Identifiers.GenerateShortId("svc"),
                OwnerId       = Identifiers.GenerateShortId("oid"),
                LeaseDuration = Duration.FromSeconds(240),
            },
            fakeLeaseManager
        );

        var locked = await sut.EnsureLocked(cancellationToken);

        // Act & Assert
        locked.IsCancellationRequested.ShouldBeFalse();

        await sut.DisposeAsync();

        locked.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async ValueTask try_unlock_signals_lease_lost_on_operation_canceled_exception(CancellationToken cancellationToken) {
        // Arrange
        var fakeLeaseManager = A.Fake<ILeaseManager>();

        A.CallTo(() => fakeLeaseManager.AcquireOrRenew(A<AcquireOrRenewLease>._, A<AcquireOrRenewLeaseRetryOptions>._, A<CancellationToken>._))
            .Returns(
                AcquireOrRenewLeaseResult.Acquired(
                    A.Fake<VersionedLease>() with {
                        Duration = Duration.FromSeconds(240)
                    }
                )
            );

        A.CallTo(() => fakeLeaseManager.Release(A<ReleaseLease>._, A<CancellationToken>._))
            .Returns(ReleaseLeaseResult.OperationCanceled());

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = Identifiers.GenerateShortId("svc"),
                OwnerId       = Identifiers.GenerateShortId("oid"),
                LeaseDuration = Duration.FromSeconds(240)
            },
            fakeLeaseManager
        );

        var locked = await sut.EnsureLocked(cancellationToken);

        // Act & Assert
        locked.IsCancellationRequested.ShouldBeFalse();

        await sut.DisposeAsync();

        locked.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Timeout(DefaultTestTimeoutMs)]
    public async ValueTask cancels_token_when_lease_is_lost(CancellationToken cancellationToken) {
        // Arrange
        var clock = new FakeClock(SystemClock.Instance.GetCurrentInstant());

        var leaseManager = new LeaseManager(
            Fixture.Client.Streams,
            clock,
            StreamTemplate,
            Fixture.LoggerFactory.CreateLogger<LeaseManager>()
        );

        var sut = new ServiceLocker(
            new ServiceLockerOptions {
                ResourceId    = Identifiers.GenerateShortId("svc"),
                OwnerId       = Identifiers.GenerateShortId("oid"),
                LeaseDuration = Duration.FromSeconds(60)
            },
            leaseManager
        );

        using var manualCancellator = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var locked = await sut.EnsureLocked(manualCancellator.Token);

        // Act
        await manualCancellator.CancelAsync();

        // Wait for the cancellation to propagate
        try {
            await Task.Delay(Timeout.Infinite, locked);
        }
        catch (OperationCanceledException) {
            // Expected
        }

        // Assert
        locked.IsCancellationRequested.ShouldBeTrue();
    }
}
