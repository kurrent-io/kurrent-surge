namespace Kurrent.Surge.Processors.Models;

/// <summary>
/// Represents a unique identifier for a processor group.
/// </summary>
[PublicAPI]
public readonly record struct ProcessorId {
    /// <summary>
    /// Represents an unset processor ID.
    /// </summary>
    public static readonly ProcessorId Unset = new("");

    ProcessorId(string value) => Value = value;

    /// <summary>
    /// The underlying string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Indicates whether this processor ID is unset.
    /// </summary>
    public bool IsUnset => this == Unset;

    /// <summary>
    /// Creates a <see cref="ProcessorId"/> from a string value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public static ProcessorId From(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("ProcessorId cannot be null or whitespace.", nameof(value))
            : new(value);

    public static implicit operator string(ProcessorId _) => _.Value;
    public static implicit operator ProcessorId(string _) => From(_);

    public override string ToString() => Value;
}
