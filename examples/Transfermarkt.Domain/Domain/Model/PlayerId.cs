namespace Transfermarkt.Domain.Model;

/// <summary>
/// Strongly-typed identifier for a player.
/// </summary>
[PublicAPI]
public readonly record struct PlayerId(Guid Value) {
    /// <summary>
    /// Represents an uninitialized or empty player identifier.
    /// </summary>
    public static readonly PlayerId None = new(Guid.Empty);

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; init; } = Value;

    /// <summary>
    /// Indicates whether this identifier is unset.
    /// </summary>
    public bool IsUnset => this == None;

    /// <summary>
    /// Creates a new unique player identifier.
    /// </summary>
    public static PlayerId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a PlayerId from a GUID value.
    /// </summary>
    public static PlayerId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("PlayerId cannot be empty.", nameof(value))
            : new PlayerId(value);

    public static implicit operator Guid(PlayerId id)   => id.Value;
    public static implicit operator PlayerId(Guid guid) => From(guid);

    public override string ToString() => Value.ToString();
}
