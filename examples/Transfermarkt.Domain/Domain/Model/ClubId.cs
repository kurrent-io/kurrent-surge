namespace Transfermarkt.Domain.Model;

/// <summary>
/// Strongly-typed identifier for a club.
/// </summary>
[PublicAPI]
public readonly record struct ClubId(Guid Value) {
    static readonly Guid UnknownGuid = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Represents an uninitialized or empty club identifier (no club specified).
    /// Use for new player registrations or retirements.
    /// </summary>
    public static readonly ClubId None = new(Guid.Empty);

    /// <summary>
    /// Represents an unknown club (club exists but identity is not known).
    /// Use when a club reference is required but the actual club is not identified.
    /// </summary>
    public static readonly ClubId Unknown = new(UnknownGuid);

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; init; } = Value;

    /// <summary>
    /// Indicates whether this identifier is unset (no club).
    /// </summary>
    public bool IsUnset => this == None;

    /// <summary>
    /// Indicates whether this identifier represents an unknown club.
    /// </summary>
    public bool IsUnknown => this == Unknown;

    /// <summary>
    /// Indicates whether this identifier is valid (not None and not Unknown).
    /// </summary>
    public bool IsValid => !IsUnset && !IsUnknown;

    /// <summary>
    /// Creates a new unique club identifier.
    /// </summary>
    public static ClubId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a ClubId from a GUID value.
    /// </summary>
    public static ClubId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ClubId cannot be empty. Use ClubId.None instead.", nameof(value))
            : value == UnknownGuid
                ? throw new ArgumentException("ClubId cannot use the reserved Unknown GUID. Use ClubId.Unknown instead.", nameof(value))
                : new ClubId(value);

    public static implicit operator Guid(ClubId id) => id.Value;

    public override string ToString() =>
        IsUnset   ? "(none)" :
        IsUnknown ? "(unknown)" :
        Value.ToString();
}
