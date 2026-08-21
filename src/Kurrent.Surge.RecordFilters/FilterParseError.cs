namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Why an expression failed to parse. Messages follow DuckDB's wording, so an expression rejected
/// here reads the same as one rejected by the server.
/// </summary>
public sealed record FilterParseError {
    FilterParseError(string errorMessage, int position, string? nearToken) {
        ErrorMessage = errorMessage;
        Position     = position;
        NearToken    = nearToken;
    }

    /// <summary>
    /// The human-readable message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// The character offset in the input where parsing stopped.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// The token parsing stopped at, or <c>null</c> when the input carried no usable token.
    /// </summary>
    public string? NearToken { get; }

    public static FilterParseError SyntaxError(string nearToken, int position = 0) =>
        new($"Error parsing filter: syntax error at or near \"{nearToken}\"", position, nearToken);

    public static FilterParseError EmptyInput() =>
        new("Error parsing filter: empty expression", 0, null);

    public FilterFormatException ToException() => new(this);
}

/// <summary>
/// Thrown when an expression cannot be parsed.
/// </summary>
public sealed class FilterFormatException(FilterParseError error) : FormatException(error.ErrorMessage) {
    /// <summary>
    /// The structured error behind this exception.
    /// </summary>
    public FilterParseError ParseError { get; } = error;
}
