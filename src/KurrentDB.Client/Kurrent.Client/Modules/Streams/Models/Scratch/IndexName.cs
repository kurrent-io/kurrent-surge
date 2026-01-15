namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the name of an index in KurrentDB.
/// </summary>
public readonly record struct IndexName {
    /// <summary>
    /// Represents an empty or unset index name.
    /// </summary>
    public static readonly IndexName None = new("");

    internal IndexName(string value) => Value = value;

    /// <summary>
    /// The index name value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the index name as a string.
    /// </summary>
    public override string ToString() => Value;

    public static IndexName Parse(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            InvalidIndexNameException.Throw("Index name cannot be null or whitespace.");

        if (!value.StartsWith(StreamName.IndexStreamsPrefix))
            InvalidIndexNameException.Throw($"Index name must start with '{StreamName.IndexStreamsPrefix}'.");

        return new(value);
    }

    public static implicit operator IndexName(string _) => Parse(_);
    public static implicit operator string(IndexName _) => _.ToString();
}

/// <summary>
/// Exception thrown when a index name is invalid.
/// </summary>
public sealed class InvalidIndexNameException(string message, Exception? innerException = null)
    : Exception(message, innerException), IThrowableException<InvalidIndexNameException> {
    public static InvalidIndexNameException Create(string message, Exception? innerException) => new(message, innerException);
}
