using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents an error that occurred while parsing a filter expression.
/// Error messages follow DuckDB format: "Error parsing filter: syntax error at or near 'TOKEN'"
/// </summary>
[PublicAPI]
public sealed record FilterParseError : IResultError {
    /// <summary>
    /// Gets the error code for filter parse errors.
    /// </summary>
    public ErrorCode ErrorCode { get; } = ErrorCode.FromType<FilterParseError>();

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the position in the input where the error occurred.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the token near which the error occurred.
    /// </summary>
    public string? NearToken { get; }

    /// <summary>
    /// Gets metadata associated with this error.
    /// </summary>
    public ImmutableDictionary<string, object?> Metadata { get; }

    FilterParseError(string errorMessage, int position, string? nearToken) {
        ErrorMessage = errorMessage;
        Position     = position;
        NearToken    = nearToken;

        var builder = ImmutableDictionary.CreateBuilder<string, object?>();
        builder["Position"] = position;

        if (nearToken is not null)
            builder["NearToken"] = nearToken;

        Metadata = builder.ToImmutable();
    }

    /// <summary>
    /// Creates a syntax error at or near the specified token.
    /// </summary>
    /// <param name="nearToken">The token near which the error occurred.</param>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="FilterParseError"/> instance.</returns>
    public static FilterParseError SyntaxError(string nearToken, int position = 0) =>
        new($"Error parsing filter: syntax error at or near \"{nearToken}\"", position, nearToken);

    /// <summary>
    /// Creates a syntax error for unexpected end of input.
    /// </summary>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="FilterParseError"/> instance.</returns>
    public static FilterParseError UnexpectedEndOfInput(int position = 0) =>
        new("Error parsing filter: syntax error at or near \"end of input\"", position, "end of input");

    /// <summary>
    /// Creates an error for empty input.
    /// </summary>
    /// <returns>A new <see cref="FilterParseError"/> instance.</returns>
    public static FilterParseError EmptyInput() =>
        new("Error parsing filter: empty expression", 0, null);

    /// <summary>
    /// Creates a custom error with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="FilterParseError"/> instance.</returns>
    public static FilterParseError Custom(string message, int position = 0) =>
        new($"Error parsing filter: {message}", position, null);

    /// <inheritdoc />
    public Exception GetOrCreateException() =>
        new FormatException(ErrorMessage);
}
