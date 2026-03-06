namespace Kurrent.Processors.Models;

/// <summary>
/// Represents a unique identifier for a worker instance within a processor group.
/// </summary>
[PublicAPI]
public readonly record struct WorkerId {
    /// <summary>
    /// Represents an unset worker ID.
    /// </summary>
    public static readonly WorkerId Unset = new("");

    WorkerId(string value) => Value = value;

    /// <summary>
    /// The underlying string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Indicates whether this worker ID is unset.
    /// </summary>
    public bool IsUnset => this == Unset;

    /// <summary>
    /// Creates a <see cref="WorkerId"/> from a string value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public static WorkerId From(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("WorkerId cannot be null or whitespace.", nameof(value))
            : new(value);

    public static implicit operator string(WorkerId _) => _.Value;
    public static implicit operator WorkerId(string _) => From(_);

    public override string ToString() => Value;
}
