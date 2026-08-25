namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// A column reference: the name as written, plus the key when one was supplied through bracket or
/// arrow syntax. A dotted path arrives here with its segments joined into the name; the schema
/// decides where the name ends and the key begins.
/// <para>
/// <see cref="Transforms"/> carries the calls wrapped around the reference, innermost first, so
/// <c>lower(trim(status))</c> reads as trim then lower.
/// </para>
/// </summary>
readonly record struct PropertyAccess(string PropertyName, string? Key, IReadOnlyList<ColumnTransform> Transforms) {
    public PropertyAccess(string propertyName, string? key) : this(propertyName, key, []) { }

    /// <summary>
    /// Whether the reference is the column itself, with nothing applied to it. Only a bare reference
    /// can carry a constraint to a store, since a transform changes what the column's value means.
    /// </summary>
    public bool IsBare => Transforms.Count == 0;
}

/// <summary>
/// A call wrapped around a column reference. Evaluated left of the operator, and rendered back into
/// the SQL it was written as.
/// </summary>
abstract record ColumnTransform;

/// <summary>
/// A function applied to the column. <paramref name="Arguments"/> holds the literals after the
/// column, so <c>substring(stream, 1, 3)</c> keeps <c>1</c> and <c>3</c>.
/// </summary>
sealed record FunctionTransform(string Name, IReadOnlyList<FilterValue> Arguments) : ColumnTransform;

/// <summary>
/// A cast, written as <c>CAST(col AS TYPE)</c>, <c>col::TYPE</c>, or <c>TRY_CAST(col AS TYPE)</c>.
/// <paramref name="Strict"/> is false only for the TRY form, which yields NULL where the others
/// raise a conversion error.
/// </summary>
sealed record CastTransform(string TargetType, bool Strict = true) : ColumnTransform;

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

/// <summary>
/// Raw bytes, produced by folding a call such as <c>from_hex('4ac7…')</c>. Renders back as the call
/// that made it, because SQL has no portable blob literal worth emitting.
/// </summary>
sealed record BlobFilterValue(byte[] Value) : FilterValue {
    public override object GetValue() => Value;
}

/// <summary>
/// A <c>TIMESTAMP '…'</c> or <c>DATE '…'</c> literal. Only a call folds one into a number; comparing a
/// timestamp against a numeric column stays an error, as it is in DuckDB.
/// </summary>
sealed record DateTimeFilterValue(DateTime Value, string TypeName) : FilterValue {
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

        // BLOB orders byte-wise in DuckDB. Without this the pair would fall through to the ToString()
        // arm, where every array reads as "System.Byte[]" and any two blobs compare equal.
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceCompareTo(rightBytes);

        // A BLOB against a VARCHAR compares the text's own bytes, so `from_hex('616263') = 'abc'` is
        // true. The hex face has to be asked for with hex(); it is not what a bare string means here.
        if (left is byte[] blob && right is string text)
            return blob.AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(text));

        if (left is string leftLiteral && right is byte[] rightBlob)
            return System.Text.Encoding.UTF8.GetBytes(leftLiteral).AsSpan().SequenceCompareTo(rightBlob);

        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        if (TryGetDateTime(left, out var leftDate) && TryGetDateTime(right, out var rightDate))
            return leftDate.CompareTo(rightDate);

        if (left is bool leftFlag && right is bool rightFlag)
            return leftFlag.CompareTo(rightFlag);

        // An enum reaches SQL as its name, so it compares against text by that name.
        if (left is Enum leftEnum && right is string enumText)
            return string.CompareOrdinal(leftEnum.ToString(), enumText);

        if (left is string leftEnumText && right is Enum rightEnum)
            return string.CompareOrdinal(leftEnumText, rightEnum.ToString());

        // A UUID compares against its text form, and DuckDB raises when the text is not one.
        if (left is Guid leftGuid && right is string guidText)
            return Guid.TryParse(guidText, out var parsedRight) ? leftGuid.CompareTo(parsedRight) : throw FilterConversionException.Cast(guidText, "UUID");

        if (left is string leftGuidText && right is Guid rightGuid)
            return Guid.TryParse(leftGuidText, out var parsedLeft) ? parsedLeft.CompareTo(rightGuid) : throw FilterConversionException.Cast(leftGuidText, "UUID");

        // No defined comparison. Comparing the ToString() forms instead is what made `created_at <
        // 'abc'` select every record; the schema rejects such a pair at parse time, so reaching here
        // means an untyped column produced a value of a shape the literal cannot meet.
        throw FilterConversionException.Compare(left, right);
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
