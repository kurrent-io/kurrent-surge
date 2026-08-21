namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Base for a generated <c>fixed</c> type: a byte array whose length the schema fixes.
/// </summary>
/// <remarks>
/// Stands in for Apache's <c>SpecificFixed</c> when Apache interop is off, so a generated type carries
/// no dependency on it. Equality is over the bytes, because two fixed values holding the same bytes are
/// the same value — the reference equality a plain class would give reports two decoded messages as
/// different.
/// </remarks>
[PublicAPI]
public abstract class AvroFixedValue : IEquatable<AvroFixedValue> {
    protected AvroFixedValue(int size) => Value = new byte[size];

    /// <summary>The bytes, exactly <see cref="Size"/> of them.</summary>
    public byte[] Value { get; }

    public int Size => Value.Length;

    public bool Equals(AvroFixedValue? other) =>
        other is not null && (ReferenceEquals(this, other) || (GetType() == other.GetType() && AvroEquality.BytesEqual(Value, other.Value)));

    public override bool Equals(object? obj) => Equals(obj as AvroFixedValue);

    public override int GetHashCode() => AvroEquality.BytesHash(Value);

    public static bool operator ==(AvroFixedValue? left, AvroFixedValue? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(AvroFixedValue? left, AvroFixedValue? right) => !(left == right);

    public override string ToString() => Convert.ToHexString(Value);
}
