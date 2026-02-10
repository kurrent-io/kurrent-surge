namespace Kurrent.Client.Streams;

[PublicAPI]
public readonly record struct Partition : IComparable<Partition>, IComparable {
    public static readonly Partition Any = new(-1);

    Partition(int value) => Value = value;

    public int Value { get; }

    /// <summary>
    /// Indicates whether this partition is <see cref="Any"/>.
    /// </summary>
    public bool IsAny => this == Any;

    public static Partition From(int value) {
        if (value < 0 && value != Any.Value)
            InvalidPartitionException.Throw($"Partition must range from 0 to {int.MaxValue}, or be {Any.Value} for 'Any'.");

        return new(value);
    }

    public static implicit operator int(Partition _) => _.Value;
    public static implicit operator Partition(int _) => From(_);

    public override string ToString() => Value.ToString();

    #region . relational members .

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

public sealed class InvalidPartitionException(string message, Exception? innerException = null)
    : Exception(message, innerException), IThrowableException<InvalidPartitionException> {
    public static InvalidPartitionException Create(string message, Exception? innerException) => new(message, innerException);
}
