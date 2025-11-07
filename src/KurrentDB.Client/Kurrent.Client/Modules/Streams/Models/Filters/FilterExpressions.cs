using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

public interface IRecordAccessor<in TRecord> {
    object?                              GetPropertyValue(TRecord record, string propertyName);
    IReadOnlyDictionary<string, object?> GetMetadata(TRecord record);
    ReadOnlySpan<char>                   GetStream(TRecord record);
    ReadOnlySpan<char>                   GetSchema(TRecord record);
    ReadOnlySpan<char>                   GetSchemaName(TRecord record);
    ReadOnlySpan<char>                   GetSchemaVersion(TRecord record);
    ReadOnlySpan<char>                   GetSchemaDataFormat(TRecord record);
    DateTime                             GetTimestamp(TRecord record);
    bool                                 TryGetMetadata(TRecord record, string key, out object? value);
}

public interface IFilterExpression {
    bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor);
    bool UsesStreamOrSchemaProperty();
}

public enum LogicalOperator {
    And, Or
}

public enum ComparisonOperator {
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

sealed record LogicalExpression(LogicalOperator Operator, IFilterExpression Left, IFilterExpression Right) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        return Operator switch {
            LogicalOperator.And => Left.Evaluate(record, accessor) && Right.Evaluate(record, accessor),
            LogicalOperator.Or  => Left.Evaluate(record, accessor) || Right.Evaluate(record, accessor),
            _                   => throw new ArgumentOutOfRangeException()
        };
    }

    public bool UsesStreamOrSchemaProperty() => Left.UsesStreamOrSchemaProperty() || Right.UsesStreamOrSchemaProperty();
}

/// <summary>
/// Generic property equality expression
/// </summary>
sealed record PropertyEqualsExpression(string PropertyName, object Value) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        var actualValue = accessor.GetPropertyValue(record, PropertyName);
        return AreEqual(actualValue, Value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema", StringComparison.OrdinalIgnoreCase);

    static bool AreEqual(object? left, object? right) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        // Handle string comparisons (case-insensitive)
        if (left is string leftStr && right is string rightStr)
            return string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);

        // Handle numeric comparisons
        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(right, CultureInfo.InvariantCulture));

        // Handle DateTime comparisons
        if (left is DateTime or DateTimeOffset && right is DateTime or DateTimeOffset)
            return ConvertToDateTimeOffset(left).Equals(ConvertToDateTimeOffset(right));

        return left.Equals(right);
    }

    static bool IsNumeric(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    static DateTimeOffset ConvertToDateTimeOffset(object value) =>
        value switch {
            DateTime dt        => dt,
            DateTimeOffset dto => dto,
            _                  => throw new ArgumentException($"Cannot convert {value.GetType().Name} to DateTimeOffset")
        };
}

/// <summary>
/// Generic property comparison expression (>, <, >=, <=, !=)
/// </summary>
sealed record PropertyComparisonExpression(string PropertyName, ComparisonOperator Operator, object Value) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        var actualValue = GetPropertyValue(record, accessor);

        return Operator switch {
            ComparisonOperator.NotEqual           => !AreEqual(actualValue, Value),
            ComparisonOperator.GreaterThan        => Compare(actualValue, Value) > 0,
            ComparisonOperator.LessThan           => Compare(actualValue, Value) < 0,
            ComparisonOperator.GreaterThanOrEqual => Compare(actualValue, Value) >= 0,
            ComparisonOperator.LessThanOrEqual    => Compare(actualValue, Value) <= 0,
            _                                     => false
        };
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);

    object? GetPropertyValue<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        return PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record).ToString(),
            "schema.name"    => accessor.GetSchemaName(record).ToString(),
            "schema.version" => accessor.GetSchemaVersion(record).ToString(),
            "timestamp"      => accessor.GetTimestamp(record),
            _ when PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase) =>
                accessor.TryGetMetadata(record, PropertyName, out var value) ? value : null,
            _ => null
        };
    }

    static bool AreEqual(object? left, object? right) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string leftStr && right is string rightStr)
            return string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);

        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(right, CultureInfo.InvariantCulture));

        if (left is DateTime or DateTimeOffset && right is DateTime or DateTimeOffset)
            return ConvertToDateTimeOffset(left).Equals(ConvertToDateTimeOffset(right));

        return left.Equals(right);
    }

    static int Compare(object? left, object? right) {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        if (left is string leftStr && right is string rightStr)
            return string.Compare(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);

        if (IsNumeric(left) && IsNumeric(right)) {
            var leftDouble  = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            var rightDouble = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            return leftDouble.CompareTo(rightDouble);
        }

        if (left is DateTime or DateTimeOffset && right is DateTime or DateTimeOffset) {
            var leftDate  = ConvertToDateTimeOffset(left);
            var rightDate = ConvertToDateTimeOffset(right);
            return leftDate.CompareTo(rightDate);
        }

        if (left is IComparable comparable)
            return comparable.CompareTo(right);

        throw new ArgumentException($"Cannot compare {left.GetType().Name} with {right.GetType().Name}");
    }

    static bool IsNumeric(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    static DateTimeOffset ConvertToDateTimeOffset(object value) =>
        value switch {
            DateTime dt        => dt,
            DateTimeOffset dto => dto,
            _                  => throw new ArgumentException($"Cannot convert {value.GetType().Name} to DateTimeOffset")
        };
}

/// <summary>
/// Generic property null check expression
/// </summary>
sealed record PropertyNullExpression(string PropertyName, bool IsNull) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        var isNullValue = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record).IsEmpty,
            "schema.name"    => accessor.GetSchemaName(record).IsEmpty,
            "schema.version" => accessor.GetSchemaVersion(record).IsEmpty,
            _ when PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase) =>
                !accessor.TryGetMetadata(record, PropertyName, out var value) || value is null,
            _ => true
        };

        return IsNull ? isNullValue : !isNullValue;
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Generic property IN expression
/// </summary>
sealed record PropertyInExpression(string PropertyName, object[] Values) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        var actualValue = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record).ToString(),
            "schema.name"    => accessor.GetSchemaName(record).ToString(),
            "schema.version" => accessor.GetSchemaVersion(record).ToString(),
            "timestamp"      => accessor.GetTimestamp(record),
            _ when PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase) =>
                accessor.TryGetMetadata(record, PropertyName, out var value) ? value : null,
            _ => null
        };

        foreach (var value in Values)
            if (AreEqual(actualValue, value))
                return true;

        return false;
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);

    static bool AreEqual(object? left, object? right) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string leftStr && right is string rightStr)
            return string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);

        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(right, CultureInfo.InvariantCulture));

        return left.Equals(right);
    }

    static bool IsNumeric(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}


/// <summary>
/// NOT IN expression for set exclusion
/// </summary>
sealed record NotInExpression(string PropertyName, object[] Values) : IFilterExpression {
    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        var actualValue = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record).ToString(),
            "schema.name"    => accessor.GetSchemaName(record).ToString(),
            "schema.version" => accessor.GetSchemaVersion(record).ToString(),
            "timestamp"      => accessor.GetTimestamp(record),
            _ when PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase) =>
                accessor.TryGetMetadata(record, PropertyName, out var value) ? value : null,
            _ => null
        };

        foreach (var value in Values)
            if (AreEqual(actualValue, value))
                return false; // Found in set, so NOT IN is false

        return true; // Not found in set, so NOT IN is true
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);

    static bool AreEqual(object? left, object? right) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        if (left is string leftStr && right is string rightStr)
            return string.Equals(leftStr, rightStr, StringComparison.OrdinalIgnoreCase);

        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(right, CultureInfo.InvariantCulture));

        return left.Equals(right);
    }

    static bool IsNumeric(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

/// <summary>
/// Contains expression for case-insensitive string contains (~)
/// </summary>
sealed record ContainsExpression(string PropertyName, string Pattern) : IFilterExpression {
    readonly Regex _regex = new(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        if (PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase)) {
            if (accessor.TryGetMetadata(record, PropertyName, out var metaValue) && metaValue is string str)
                return _regex.IsMatch(str);
            return false;
        }

        var value = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record),
            "schema.name"    => accessor.GetSchemaName(record),
            "schema.version" => accessor.GetSchemaVersion(record),
            _                => ReadOnlySpan<char>.Empty
        };

        return _regex.IsMatch(value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Contains exact expression for case-sensitive string contains (~*)
/// </summary>
sealed record ContainsExactExpression(string PropertyName, string Pattern) : IFilterExpression {
    readonly Regex _regex = new(Pattern, RegexOptions.Compiled);

    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        if (PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase)) {
            if (accessor.TryGetMetadata(record, PropertyName, out var metaValue) && metaValue is string str)
                return _regex.IsMatch(str);
            return false;
        }

        var value = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record),
            "schema.name"    => accessor.GetSchemaName(record),
            "schema.version" => accessor.GetSchemaVersion(record),
            _                => ReadOnlySpan<char>.Empty
        };

        return _regex.IsMatch(value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Not contains expression for case-insensitive string does not contain (!~)
/// </summary>
sealed record NotContainsExpression(string PropertyName, string Pattern) : IFilterExpression {
    readonly Regex _regex = new(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        if (PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase)) {
            if (accessor.TryGetMetadata(record, PropertyName, out var metaValue) && metaValue is string str)
                return !_regex.IsMatch(str);
            return true; // If no metadata value, it doesn't contain the pattern
        }

        var value = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record),
            "schema.name"    => accessor.GetSchemaName(record),
            "schema.version" => accessor.GetSchemaVersion(record),
            _                => ReadOnlySpan<char>.Empty
        };

        return !_regex.IsMatch(value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Not contains exact expression for case-sensitive string does not contain (!~*)
/// </summary>
sealed record NotContainsExactExpression(string PropertyName, string Pattern) : IFilterExpression {
    readonly Regex _regex = new(Pattern, RegexOptions.Compiled);

    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        if (PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase)) {
            if (accessor.TryGetMetadata(record, PropertyName, out var metaValue) && metaValue is string str)
                return !_regex.IsMatch(str);
            return true; // If no metadata value, it doesn't contain the pattern
        }

        var value = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record),
            "schema.name"    => accessor.GetSchemaName(record),
            "schema.version" => accessor.GetSchemaVersion(record),
            _                => ReadOnlySpan<char>.Empty
        };

        return !_regex.IsMatch(value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Generic property regex expression
/// </summary>
sealed record PropertyRegexExpression(string PropertyName, string Pattern, bool CaseSensitive = true) : IFilterExpression {
    readonly Regex _regex = new(
        Pattern,
        RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
    );

    public bool Evaluate<TRecord>(in TRecord record, IRecordAccessor<TRecord> accessor) {
        // Handle metadata properties specially since they need complex logic
        if (PropertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase)) {
            if (accessor.TryGetMetadata(record, PropertyName, out var metaValue) && metaValue is string str)
                return _regex.IsMatch(str);

            return false;
        }

        var value = PropertyName.ToLowerInvariant() switch {
            "stream"         => accessor.GetStream(record),
            "schema.name"    => accessor.GetSchemaName(record),
            "schema.version" => accessor.GetSchemaVersion(record),
            _                => ReadOnlySpan<char>.Empty
        };

        return _regex.IsMatch(value);
    }

    public bool UsesStreamOrSchemaProperty() =>
        PropertyName.Equals("stream", StringComparison.OrdinalIgnoreCase) ||
        PropertyName.StartsWith("schema.", StringComparison.OrdinalIgnoreCase);
}
