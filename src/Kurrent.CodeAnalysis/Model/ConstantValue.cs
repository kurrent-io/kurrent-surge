namespace Kurrent.CodeAnalysis;

/// <summary>
/// Represents a compile-time constant surfaced from attribute arguments.
/// </summary>
public abstract record ConstantValue(string TypeFullName) {
    public sealed record Primitive(object? BoxedValue, string TypeFullName) : ConstantValue(TypeFullName) {
        public string AsString  => BoxedValue?.ToString() ?? "";
        public int    AsInt32   => Convert.ToInt32(BoxedValue);
        public long   AsInt64   => Convert.ToInt64(BoxedValue);
        public bool   AsBoolean => Convert.ToBoolean(BoxedValue);
        public double AsDouble  => Convert.ToDouble(BoxedValue);
        public T?     As<T>()   => BoxedValue is T t ? t : default;
    }

    public sealed record TypeOf(string ReferencedTypeFullName, string TypeFullName) : ConstantValue(TypeFullName);

    public sealed record Array(ConstantValue[] Values, string TypeFullName) : ConstantValue(TypeFullName) {
        public bool Equals(Array? other) {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return TypeFullName == other.TypeFullName
                && Values.AsSpan().SequenceEqual(other.Values.AsSpan());
        }

        public override int GetHashCode() =>
            HashCode.New
                .Combine(TypeFullName)
                .Combine(Values);
    }

    public sealed record Enum(long UnderlyingValue, string? MemberName, string TypeFullName) : ConstantValue(TypeFullName);

    public sealed record Null(string TypeFullName) : ConstantValue(TypeFullName);
}
