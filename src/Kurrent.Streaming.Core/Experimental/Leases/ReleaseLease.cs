namespace Kurrent.Surge.Leases;

[PublicAPI]
public record ReleaseLease {
    public required string       ResourceId { get; init; }
    public required string       OwnerId    { get; init; }
    public required LeaseVersion Version    { get; init; }
}

public enum ReleaseLeaseResultType {
    Unknown           = 0,
    Released          = 1,
    Taken             = 2,
    VersionMismatch   = 3,
    Expired           = 4,
    NotFound          = 5,
    OperationCanceled = 6
}

[PublicAPI]
record ReleaseLeaseResult {
    public ReleaseLeaseResult(ReleaseLeaseResultType type, VersionedLease? lease = null) {
        Type     = type;
        Lease    = lease ?? VersionedLease.None;
    }

    public ReleaseLeaseResultType Type  { get; init; } = ReleaseLeaseResultType.Unknown;
    public VersionedLease         Lease { get; init; } = VersionedLease.None;

    public bool Failure => Type is ReleaseLeaseResultType.Taken
                                or ReleaseLeaseResultType.VersionMismatch
                                or ReleaseLeaseResultType.NotFound
                                or ReleaseLeaseResultType.OperationCanceled;

    public bool Success => !Failure;

    public static implicit operator bool(ReleaseLeaseResult                   _) => _.Success;
    public static implicit operator VersionedLease(ReleaseLeaseResult         _) => _.Lease;
    public static implicit operator ReleaseLeaseResultType(ReleaseLeaseResult _) => _.Type;

    public static ReleaseLeaseResult Released() =>
        new(ReleaseLeaseResultType.Released);

    public static ReleaseLeaseResult Taken(VersionedLease lease) =>
        new(ReleaseLeaseResultType.Taken, lease);

    public static ReleaseLeaseResult VersionMismatch(VersionedLease lease) =>
        new(ReleaseLeaseResultType.VersionMismatch, lease);

    public static ReleaseLeaseResult Expired(VersionedLease lease) =>
        new(ReleaseLeaseResultType.Expired, lease);

    public static ReleaseLeaseResult NotFound() =>
        new(ReleaseLeaseResultType.NotFound);

    public static ReleaseLeaseResult OperationCanceled() =>
        new(ReleaseLeaseResultType.OperationCanceled);
}