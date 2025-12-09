namespace Kurrent.Client.Streams;

[PublicAPI]
public readonly record struct PartitionNumber : IComparable<PartitionNumber>, IComparable {
    public static readonly PartitionNumber Any = new(-1);

    PartitionNumber(long value) => Value = value;

    public long Value { get; }

    public static PartitionNumber From(long value) {
        if (value < 0 && value != Any.Value)
            throw new InvalidPartitionNumber(value);

        return new(value);
    }

    public static implicit operator long(PartitionNumber _) => _.Value;
    public static implicit operator PartitionNumber(long _) => From(_);

    public override string ToString() => Value.ToString();

    #region . relational members .

    public int CompareTo(PartitionNumber other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) {
        if (ReferenceEquals(null, obj)) return 1;

        return obj is PartitionNumber other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(PartitionNumber)}");
    }

    public static bool operator <(PartitionNumber left, PartitionNumber right)  => left.CompareTo(right) < 0;
    public static bool operator >(PartitionNumber left, PartitionNumber right)  => left.CompareTo(right) > 0;
    public static bool operator <=(PartitionNumber left, PartitionNumber right) => left.CompareTo(right) <= 0;
    public static bool operator >=(PartitionNumber left, PartitionNumber right) => left.CompareTo(right) >= 0;

    #endregion
}

public class InvalidPartitionNumber(long? value) : ArgumentException($"Partition Number is invalid: {value}");
