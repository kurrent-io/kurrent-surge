using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a filter that can be applied to records.
/// </summary>
[PublicAPI]
public readonly struct RecordFilter(IFilterExpression expression) {
    /// <summary>
    /// A filter that matches no records (empty filter).
    /// </summary>
    public static readonly RecordFilter None = default;

    /// <summary>
    /// Gets the underlying filter expression.
    /// </summary>
    public IFilterExpression? Expression { get; init; } = expression;

    /// <summary>
    /// Gets a value indicating whether this filter is empty (matches all records).
    /// </summary>
    [MemberNotNullWhen(false, nameof(Expression))]
    public bool IsEmpty => Expression is null;

    /// <summary>
    /// Evaluates whether the specified record matches this filter.
    /// </summary>
    /// <param name="record">The record to evaluate.</param>
    /// <returns><c>true</c> if the record matches or filter is empty; otherwise, <c>false</c>.</returns>
    public bool Matches(Record record) => IsEmpty || Expression.IsMatch(record);

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "None" : Expression?.ToString() ?? "Invalid";

    /// <summary>
    /// Creates a new <see cref="RecordFilter"/> from the specified expression.
    /// </summary>
    /// <param name="expression">The filter expression.</param>
    /// <returns>A new <see cref="RecordFilter"/> instance.</returns>
    public static RecordFilter Create(IFilterExpression expression) => new(expression);

    /// <summary>
    /// Parses a filter expression string into a <see cref="RecordFilter"/>.
    /// </summary>
    /// <param name="input">The filter expression string to parse.</param>
    /// <returns>
    /// A result containing either the parsed <see cref="RecordFilter"/> on success,
    /// or a <see cref="ValidationError"/> on failure.
    /// </returns>
    public static Result<RecordFilter, ValidationError> Parse(string input) {
        if (string.IsNullOrWhiteSpace(input))
            return None;

        var parseResult = Parser.Parse(input);

        if (parseResult.IsFailure)
            return ValidationError.FromProblem("FILTER_PARSE_ERROR", parseResult.Error.ErrorMessage);

        return Create(parseResult.Value);
    }

    static readonly FilterExpressionParser Parser = new();
}
