using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

public interface IFilterBuilder {
    IStreamOperators    Stream    { get; }
    ISchemaOperators    Schema    { get; }
    IMetadataOperators  Metadata  { get; }
    ITimestampOperators Timestamp { get; }
}

public interface IStreamOperators {
    IFilterExpression Equals(string streamName);
    IFilterExpression StartsWith(string prefix);
    IFilterExpression Matches(string pattern);
    IFilterExpression In(params string[] values);
}

public interface ISchemaOperators {
    IFilterExpression Equals(string schemaName);
    IFilterExpression StartsWith(string prefix);
    IFilterExpression Matches(string pattern);
    IFilterExpression In(params string[] values);
}

public interface IMetadataOperators {
    IMetadataComparisons Property(string key);
}

public interface IMetadataComparisons {
    IFilterExpression EqualsValue(object value);
    IFilterExpression NotEquals(object value);
    IFilterExpression GreaterThan(object value);
    IFilterExpression LessThan(object value);
    IFilterExpression IsNull();
    IFilterExpression IsNotNull();
}

public interface ITimestampOperators {
    IFilterExpression Equals(DateTimeOffset timestamp);
    IFilterExpression After(DateTimeOffset timestamp);
    IFilterExpression Before(DateTimeOffset timestamp);
    IFilterExpression Between(DateTimeOffset start, DateTimeOffset end);
}

sealed class FilterBuilder : IFilterBuilder {
    public IStreamOperators    Stream    { get; } = new StreamOperators();
    public ISchemaOperators    Schema    { get; } = new SchemaOperators();
    public IMetadataOperators  Metadata  { get; } = new MetadataOperators();
    public ITimestampOperators Timestamp { get; } = new TimestampOperators();
}

sealed class StreamOperators : IStreamOperators {
    public IFilterExpression Equals(string streamName)  => new PropertyEqualsExpression("Stream", streamName);
    public IFilterExpression StartsWith(string prefix)  => new PropertyRegexExpression("Stream", $"^{Regex.Escape(prefix)}.*");
    public IFilterExpression Matches(string pattern)    => new PropertyRegexExpression("Stream", pattern);
    public IFilterExpression In(params string[] values) => new PropertyInExpression("Stream", values.Cast<object>().ToArray());
}

sealed class SchemaOperators : ISchemaOperators {
    public IFilterExpression Equals(string schemaName)  => new PropertyEqualsExpression("Schema", schemaName);
    public IFilterExpression StartsWith(string prefix)  => new PropertyRegexExpression("Schema", $"^{Regex.Escape(prefix)}.*");
    public IFilterExpression Matches(string pattern)    => new PropertyRegexExpression("Schema", pattern);
    public IFilterExpression In(params string[] values) => new PropertyInExpression("Schema", values.Cast<object>().ToArray());
}

sealed class MetadataOperators : IMetadataOperators {
    public IMetadataComparisons Property(string key) => new MetadataComparisons(key);
}

sealed class MetadataComparisons(string PropertyKey) : IMetadataComparisons {
    public IFilterExpression EqualsValue(object value) => new PropertyEqualsExpression($"metadata.{PropertyKey}", value);
    public IFilterExpression NotEquals(object value)   => new PropertyComparisonExpression($"metadata.{PropertyKey}", ComparisonOperator.NotEqual, value);
    public IFilterExpression GreaterThan(object value) => new PropertyComparisonExpression($"metadata.{PropertyKey}", ComparisonOperator.GreaterThan, value);
    public IFilterExpression LessThan(object value)    => new PropertyComparisonExpression($"metadata.{PropertyKey}", ComparisonOperator.LessThan, value);
    public IFilterExpression IsNull()                  => new PropertyNullExpression($"metadata.{PropertyKey}", true);
    public IFilterExpression IsNotNull()               => new PropertyNullExpression($"metadata.{PropertyKey}", false);
}

sealed class TimestampOperators : ITimestampOperators {
    public IFilterExpression Equals(DateTimeOffset timestamp) => new PropertyEqualsExpression("Timestamp", timestamp);
    public IFilterExpression After(DateTimeOffset timestamp)  => new PropertyComparisonExpression("Timestamp", ComparisonOperator.GreaterThan, timestamp);
    public IFilterExpression Before(DateTimeOffset timestamp) => new PropertyComparisonExpression("Timestamp", ComparisonOperator.LessThan, timestamp);

    public IFilterExpression Between(DateTimeOffset start, DateTimeOffset end) =>
        new LogicalExpression(
            LogicalOperator.And,
            new PropertyComparisonExpression("Timestamp", ComparisonOperator.GreaterThanOrEqual, start),
            new PropertyComparisonExpression("Timestamp", ComparisonOperator.LessThanOrEqual, end)
        );
}

public static class All {
    public static IFilterExpression Of(params IFilterExpression[] expressions) {
        if (expressions.Length == 0) throw new ArgumentException("At least one expression is required");

        if (expressions.Length == 1) return expressions[0];

        return expressions.Skip(1).Aggregate(expressions[0], (acc, expr) => new LogicalExpression(LogicalOperator.And, acc, expr));
    }
}

public static class Any {
    public static IFilterExpression Of(params IFilterExpression[] expressions) {
        if (expressions.Length == 0) throw new ArgumentException("At least one expression is required");

        if (expressions.Length == 1) return expressions[0];

        return expressions.Skip(1).Aggregate(expressions[0], (acc, expr) => new LogicalExpression(LogicalOperator.Or, acc, expr));
    }
}

public static class Not {
    public static IFilterExpression Of(IFilterExpression expression) => new NotExpression(expression);
}

/// <summary>
/// Negates a filter expression
/// </summary>
sealed record NotExpression(IFilterExpression Inner) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) => !Inner.Evaluate(record, accessor);

    public bool UsesStreamOrSchemaProperty() => Inner.UsesStreamOrSchemaProperty();
}
