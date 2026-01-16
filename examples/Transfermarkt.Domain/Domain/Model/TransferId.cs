namespace Transfermarkt.Domain;

/// <summary>
/// Strongly-typed identifier for a transfer.
/// </summary>
[PublicAPI]
public readonly record struct TransferId {
    /// <summary>
    /// Represents an uninitialized or empty transfer identifier.
    /// </summary>
    public static readonly TransferId None = new(Guid.Empty);

    TransferId(Guid value) => Value = value;

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Indicates whether this identifier is unset.
    /// </summary>
    public bool IsUnset => this == None;

    /// <summary>
    /// Creates a new unique transfer identifier.
    /// </summary>
    public static TransferId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a TransferId from a GUID value.
    /// </summary>
    public static TransferId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("TransferId cannot be empty.", nameof(value))
            : new TransferId(value);

    public static implicit operator Guid(TransferId id)   => id.Value;
    public static implicit operator TransferId(Guid guid) => From(guid);

    public override string ToString() => Value.ToString();
}
