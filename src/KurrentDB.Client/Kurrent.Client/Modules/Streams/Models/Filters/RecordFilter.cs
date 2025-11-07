using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

public readonly struct RecordFilter {
    public static readonly RecordFilter None = default;

    public IFilterExpression? Expression { get; init; }

    public RecordFilter(IFilterExpression expression) => Expression = expression;

    [MemberNotNullWhen(false, nameof(Expression))]
    public bool IsEmpty => Expression is null;

    public bool Matches<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) => !IsEmpty && Expression.Evaluate(record, accessor);

    public bool UsesStreamOrSchemaProperty() => !IsEmpty && Expression.UsesStreamOrSchemaProperty();

    // Note: Implicit conversion from interface to struct is not allowed in C#
    // Use RecordFilter.Create(expression) instead

    public override string ToString() => IsEmpty ? "None" : Expression?.ToString() ?? "Invalid";

    public static RecordFilter Create(IFilterExpression expression) => new(expression);

    public static Result<RecordFilter, ValidationError> Parse(string input) {
        if (string.IsNullOrWhiteSpace(input))
            return None;

        var parseResult = FilterParser.TryParse(input);
        if (parseResult.IsEmpty)
            return None;

        if (!parseResult.IsSuccess)
            return ValidationError.FromProblem("FILTER_PARSE_ERROR", parseResult.Error!);

        return Create(parseResult.Expression!);
    }
}
