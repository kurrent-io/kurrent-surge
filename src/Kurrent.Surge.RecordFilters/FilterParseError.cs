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

    public static FilterParseError UndeclaredColumn(string column, string target) =>
        new($"Error parsing filter: no column named \"{column}\" is declared in FilterSchema<{target}>", 0, column);

    /// <summary>
    /// A literal whose type cannot be compared against the column's at all — a number against text,
    /// for instance. DuckDB refuses this when it binds the query, before any row is read.
    /// </summary>
    public static FilterParseError IncomparableTypes(string column, string columnType, string literalType) =>
        new($"Error parsing filter: Cannot compare values of type {columnType} and type {literalType} for column \"{column}\" - an explicit cast is required", 0, column);

    /// <summary>
    /// A literal whose type could be compared but whose value cannot convert — <c>'abc'</c> against a
    /// numeric column. The literal is constant, so the failure is settled here rather than per record.
    /// </summary>
    public static FilterParseError LiteralConversion(string literal, string targetType) =>
        new($"Error parsing filter: Conversion Error: Could not convert string '{literal}' to {targetType}", 0, literal);

    /// <summary>
    /// An expression names a parameter nothing was bound to.
    /// </summary>
    public static FilterParseError UnboundParameter(string name, string bound) =>
        new($"Error parsing filter: no value is bound to parameter \"@{name}\"{bound}", 0, name);

    /// <summary>
    /// A value was bound that the expression never names — almost always a typo on one side.
    /// </summary>
    public static FilterParseError UnusedParameter(string name) =>
        new($"Error parsing filter: parameter \"@{name}\" is bound but never used", 0, name);

    public static FilterParseError ScalarColumnHasNoFields(string column, string field) =>
        new($"Error parsing filter: cannot extract field '{field}' from column \"{column}\" because it is not a map or json column", 0, column);

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

/// <summary>
/// Thrown when a value cannot be converted while the expression runs, matching DuckDB, where a failed
/// <c>CAST</c> fails the query rather than yielding NULL. Use <c>TRY_CAST</c> for the NULL form.
/// </summary>
public sealed class FilterConversionException(string message) : InvalidOperationException(message) {
    public static FilterConversionException Cast(object? value, string target) =>
        new($"Conversion Error: Could not convert {Describe(value)} to {target.ToUpperInvariant()}");

    public static FilterConversionException Compare(object? left, object? right) =>
        new($"Conversion Error: Cannot compare {Describe(left)} with {Describe(right)}");

    static string Describe(object? value) =>
        value is null ? "NULL" : $"{value.GetType().Name.ToLowerInvariant()} '{value}'";
}
