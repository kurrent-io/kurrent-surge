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
    byte[] _value;

    protected AvroFixedValue(int size) {
        Size   = size;
        _value = new byte[size];
    }

    /// <summary>The byte count the schema fixes.</summary>
    public int Size { get; }

    /// <summary>The bytes, exactly <see cref="Size"/> of them.</summary>
    /// <remarks>
    /// The length is enforced on assignment rather than trusted. The encoder writes as many bytes as
    /// this array holds while the decoder reads the count the schema declares, so a wrong length does
    /// not merely misreport <see cref="Size"/> — it desynchronises every field written after it.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not exactly <see cref="Size"/> bytes.</exception>
    public byte[] Value {
        get => _value;
        init => _value = value.Length == Size
            ? value
            : throw new ArgumentException($"A fixed of size {Size} cannot hold {value.Length} bytes.", nameof(value));
    }

    public bool Equals(AvroFixedValue? other) =>
        other is not null && (ReferenceEquals(this, other) || (GetType() == other.GetType() && AvroEquality.BytesEqual(Value, other.Value)));

    public override bool Equals(object? obj) => Equals(obj as AvroFixedValue);

    public override int GetHashCode() => AvroEquality.BytesHash(Value);

    public static bool operator ==(AvroFixedValue? left, AvroFixedValue? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(AvroFixedValue? left, AvroFixedValue? right) => !(left == right);

    public override string ToString() => Convert.ToHexString(Value);
}
