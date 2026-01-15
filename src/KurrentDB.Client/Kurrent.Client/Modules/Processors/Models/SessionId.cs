namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a unique session identifier assigned by the server when a worker connects.
/// </summary>
[PublicAPI]
public readonly record struct SessionId {
    /// <summary>
    /// Represents an unset session (e.g., worker never connected).
    /// </summary>
    public static readonly SessionId None = new(Guid.Empty);

    SessionId(Guid value) => Value = value;

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Indicates whether this session ID is unset.
    /// </summary>
    public bool IsNone => this == None;

    /// <summary>
    /// Creates a <see cref="SessionId"/> from a GUID value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/>.</exception>
    public static SessionId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("SessionId cannot be empty.", nameof(value))
            : new(value);

    public static implicit operator Guid(SessionId _) => _.Value;
    public static implicit operator SessionId(Guid _) => From(_);

    public override string ToString() => Value.ToString();
}
