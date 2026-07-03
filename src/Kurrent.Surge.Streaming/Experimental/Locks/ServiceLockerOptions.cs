using Kurrent.Surge.Leases;
using NodaTime;

namespace Kurrent.Processors.Locks;

record ServiceLockerOptions {
    public required string   ResourceId     { get; init; }
    public required string   OwnerId        { get; init; }
    public required Duration LeaseDuration  { get; init; } = Duration.FromSeconds(30);

    public Duration ReleaseTimeout { get; init; } = Duration.FromSeconds(10);

    public AcquireOrRenewLeaseRetryOptions Retry { get; init; } = new();
}
