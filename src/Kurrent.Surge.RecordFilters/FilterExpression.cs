namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// A column reference: the name as written, plus the key when one was supplied through bracket or
/// arrow syntax. A dotted path arrives here with its segments joined into the name; the schema
/// decides where the name ends and the key begins.
/// </summary>
readonly record struct PropertyAccess(string PropertyName, string? Key);

/// <summary>
/// A node in a parsed filter expression. The tree is inert data — it carries what was written and
/// nothing else. <see cref="FilterCompiler"/> turns it into a delegate and <see cref="DuckDbRenderer"/>
/// turns it into SQL; both run once, and neither belongs to the tree.
/// </summary>
interface IFilterExpression;

#region ->> Literals <<-

abstract record FilterValue {
    public abstract object? GetValue();
}

sealed record StringFilterValue(string Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record NumberFilterValue(decimal Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record BooleanFilterValue(bool Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record NullFilterValue : FilterValue {
    public static readonly NullFilterValue Instance = new();

    public override object? GetValue() => null;
}

enum ComparisonOperator {
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

#endregion

#region ->> Nodes <<-

sealed record ComparisonExpression(PropertyAccess Property, ComparisonOperator Op, FilterValue Value) : IFilterExpression;

sealed record AndExpression(IFilterExpression Left, IFilterExpression Right) : IFilterExpression;

sealed record OrExpression(IFilterExpression Left, IFilterExpression Right) : IFilterExpression;

sealed record NotExpression(IFilterExpression Inner) : IFilterExpression;

sealed record IsNullExpression(PropertyAccess Property, bool Negated) : IFilterExpression;

sealed record InExpression(PropertyAccess Property, IReadOnlyList<FilterValue> Values, bool Negated) : IFilterExpression;

sealed record BetweenExpression(PropertyAccess Property, FilterValue Lower, FilterValue Upper, bool Negated) : IFilterExpression;

sealed record LikeExpression(PropertyAccess Property, string Pattern, bool CaseInsensitive, bool Negated) : IFilterExpression;

#endregion

/// <summary>
/// Cross-type comparison for values whose type is not known ahead of time — a metadata entry or a
/// JSON leaf. A column with a declared type never reaches here; it binds a typed comparison instead.
/// <para>
/// Strings compare ordinally: the server evaluates the same expression as SQL, where <c>=</c> on text
/// is case-sensitive, and a case-insensitive client would disagree with it on identical input.
/// </para>
/// </summary>
static class ValueComparer {
    public static int Compare(object? left, object? right) {
        if (left is null && right is null) return 0;
        if (left is null)                  return -1;
        if (right is null)                 return 1;

        if (left is string leftText && right is string rightText)
            return string.CompareOrdinal(leftText, rightText);

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        if (TryGetDateTime(left, out var leftDate) && TryGetDateTime(right, out var rightDate))
            return leftDate.CompareTo(rightDate);

        if (left is bool leftFlag && right is bool rightFlag)
            return leftFlag.CompareTo(rightFlag);

        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    static bool TryGetNumber(object value, out decimal result) {
        switch (value) {
            case decimal d:                                    result = d;            return true;
            case int i:                                        result = i;            return true;
            case long l:                                       result = l;            return true;
            case double dbl:                                   result = (decimal)dbl; return true;
            case float f:                                      result = (decimal)f;   return true;
            case string s when decimal.TryParse(s, out var p): result = p;            return true;
            default:                                           result = 0;            return false;
        }
    }

    static bool TryGetDateTime(object value, out DateTime result) {
        switch (value) {
            case DateTime dt:                                   result = dt;              return true;
            case DateTimeOffset dto:                            result = dto.UtcDateTime; return true;
            case string s when DateTime.TryParse(s, out var p): result = p;               return true;
            default:                                            result = default;         return false;
        }
    }
}
