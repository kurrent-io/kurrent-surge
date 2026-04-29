using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public readonly record struct Partition : IComparable<Partition>, IComparable {
    public static readonly Partition Any = new(-1);

    Partition(int value) => Value = value;

    public int Value { get; }
    
    public bool IsAny => this == Any;

    public override string ToString() =>
        Value switch{
            -1   => "[Any]",
            >= 0 => $"[{Value}]"
        };

    public static Partition From(int partitionValue) =>
        partitionValue switch {
            -1   => Any,
            >= 0 => new(partitionValue),
            _    => throw new InvalidPartition(partitionValue)
        };

    public static implicit operator int(Partition partition) => partition.Value;

    public static implicit operator Partition(int partition) => From(partition);

    #region ->> relational members <<-

    public int CompareTo(Partition other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) {
        if (ReferenceEquals(null, obj)) return 1;

        return obj is Partition other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Partition)}");
    }

    public static bool operator <(Partition left, Partition right)  => left.CompareTo(right) < 0;
    public static bool operator >(Partition left, Partition right)  => left.CompareTo(right) > 0;
    public static bool operator <=(Partition left, Partition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Partition left, Partition right) => left.CompareTo(right) >= 0;

    #endregion
}

public sealed class InvalidPartition(int value) : KurrentException(
    ErrorCode.FromType<InvalidPartition>(),
    $"Invalid partition value: {value}. Partition must be greater than or equal to 0, or equal to -1 (for Any)."
);
