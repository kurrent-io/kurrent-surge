using NodaTime;

namespace Kurrent.Surge.Leases;

[PublicAPI]
public record Lease {
    // public static readonly Lease None = new() {
    //     ResourceId        = "",
    //     ResourceNamespace = "",
    //     AcquiredBy        = "",
    //     AcquiredAt        = default,
    //     Duration          = default
    // };

    public required string   ResourceId { get; init; }
    public required string   AcquiredBy { get; init; }
    public required Instant  AcquiredAt { get; init; }
    public required Duration Duration   { get; init; }

    public Instant ExpiresAt => AcquiredAt.Plus(Duration);

    /// <summary>
    /// Determines whether the lease has expired at a given point in time, taking into account a skew.
    /// </summary>
    /// <param name="now">The current instant in time.</param>
    /// <param name="skew">The skew duration. This is added to the ExpiresAt time to create a window in which the lease is still considered expired even if the current time is before the ExpiresAt time plus skew.</param>
    /// <returns>True if the lease has expired, false otherwise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the skew is less than zero.</exception>
    public bool HasExpired(Instant now, Duration skew) {
        if (skew < Duration.Zero)
            throw new ArgumentOutOfRangeException(nameof(skew), "Skew cannot be negative");

        return now > ExpiresAt.Plus(skew);
    }

    public bool HasNotExpired(Instant now, Duration skew) => !HasExpired(now, skew);

    /// <summary>
    /// Determines whether the lease has expired at a given point in time.
    /// </summary>
    /// <param name="now">The current instant in time.</param>
    /// <returns>True if the lease has expired, false otherwise.</returns>
    public bool HasExpired(Instant now) => HasExpired(now, Duration.Zero);

    public bool HasNotExpired(Instant now) => !HasExpired(now);

    public bool IsOwnedBy(string ownerId) => AcquiredBy == ownerId;

    public bool IsNotOwnedBy(string ownerId) => !IsOwnedBy(ownerId);
}

[PublicAPI]
public record VersionedLease : Lease {
    public static readonly VersionedLease None = new() {
        ResourceId = "",
        AcquiredBy = "",
        AcquiredAt = default,
        Duration   = default,
        Version    = LeaseVersion.Unset
    };

    public LeaseVersion Version { get; init; } = LeaseVersion.Unset;

    public bool IsAcquired => string.IsNullOrWhiteSpace(AcquiredBy)
                           && AcquiredAt != default
                           && Duration != default
                           && Version != LeaseVersion.Unset;

    public bool IsNone => this == None;

    public bool VersionMismatch(LeaseVersion expected) => Version != expected;
}

[PublicAPI]
public readonly record struct LeaseVersion : IComparable<LeaseVersion>, IComparable {
    public static readonly LeaseVersion Unset = new(-1);

    LeaseVersion(long value) => Value = value;

    internal long Value { get; }

    public static LeaseVersion From(long value) {
        if (value < 0 && value != Unset.Value)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Lease version is invalid");

        return new(value);
    }

    public bool IsUnset => this == Unset;
    public bool IsSet   => !IsUnset;

    public LeaseVersion Next() => Value + 1;

    public static implicit operator long(LeaseVersion _) => _.Value;
    public static implicit operator LeaseVersion(long _) => From(_);

    public override string ToString() => Value.ToString();

    #region . relational members .

    public int CompareTo(LeaseVersion other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) {
        if (ReferenceEquals(null, obj)) return 1;

        return obj is LeaseVersion other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(LeaseVersion)}");
    }

    public static bool operator <(LeaseVersion  left, LeaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(LeaseVersion  left, LeaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(LeaseVersion left, LeaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(LeaseVersion left, LeaseVersion right) => left.CompareTo(right) >= 0;

    #endregion
}
