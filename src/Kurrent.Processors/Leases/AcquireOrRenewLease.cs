namespace Kurrent.Processors.Leases;

[PublicAPI]
public record AcquireOrRenewLease {
    public required string   ResourceId { get; init; }
    public required string   OwnerId    { get; init; }
    public required Duration Duration   { get; init; }

    public LeaseVersion Version { get; init; } = LeaseVersion.Unset;
}

[PublicAPI]
public record AcquireOrRenewLeaseRetryOptions {
    public Duration Timeout   { get; init; } = Duration.FromSeconds(30);
    public Duration Delay     { get; init; } = Duration.FromSeconds(3);
    public double   Fuzziness { get; init; } = 0.2;
}

public enum AcquireOrRenewLeaseResultType {
    Unknown           = 0,
    Acquired          = 1,
    Renewed           = 2,
    Taken             = 3,
    VersionMismatch   = 4,
    Timeout           = 5,
    OperationCanceled = 6
}

[PublicAPI]
public record AcquireOrRenewLeaseResult {
    public AcquireOrRenewLeaseResult(AcquireOrRenewLeaseResultType type, VersionedLease? lease = null) {
        Type     = type;
        Lease    = lease ?? VersionedLease.None;
    }

    public AcquireOrRenewLeaseResultType Type  { get; init; } = AcquireOrRenewLeaseResultType.Unknown;
    public VersionedLease                Lease { get; init; } = VersionedLease.None;

    public bool Success => Type is AcquireOrRenewLeaseResultType.Acquired or AcquireOrRenewLeaseResultType.Renewed;
    public bool Failure => !Success;

    public static implicit operator bool(AcquireOrRenewLeaseResult                          _) => _.Success;
    public static implicit operator VersionedLease(AcquireOrRenewLeaseResult                _) => _.Lease;
    public static implicit operator AcquireOrRenewLeaseResultType(AcquireOrRenewLeaseResult _) => _.Type;

    public static AcquireOrRenewLeaseResult Acquired(VersionedLease lease) =>
        new(AcquireOrRenewLeaseResultType.Acquired, lease);

    public static AcquireOrRenewLeaseResult Renewed(VersionedLease lease) =>
        new(AcquireOrRenewLeaseResultType.Renewed, lease);

    public static AcquireOrRenewLeaseResult Taken(VersionedLease lease) =>
        new(AcquireOrRenewLeaseResultType.Taken, lease);

    public static AcquireOrRenewLeaseResult VersionMismatch(VersionedLease lease) =>
        new(AcquireOrRenewLeaseResultType.VersionMismatch, lease);

    public static AcquireOrRenewLeaseResult Timeout() =>
        new(AcquireOrRenewLeaseResultType.Timeout);

    public static AcquireOrRenewLeaseResult OperationCanceled() =>
        new(AcquireOrRenewLeaseResultType.OperationCanceled);
}
