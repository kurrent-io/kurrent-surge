namespace Kurrent.Client.Streams.Checkpoints;

/// <summary>
/// Represents a unique identifier used for sequencing items.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="SequenceId"/> is implemented as a readonly record struct backed by an unsigned long value.
/// It is intended to provide type safety and to ensure that valid sequencing is maintained within streams.
/// </para>
/// <para>
/// SequenceId has a static value <see cref="None"/> which represents a default sequence identifier with a value of 0.
/// Using this as a default ensures that invalid identifiers can be distinguished from valid values.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct SequenceId : IComparable<SequenceId>, IComparable {
	public static readonly SequenceId None = new(0);

	SequenceId(ulong value) => Value = value;

	public ulong Value { get; }

	public static SequenceId From(ulong value) =>
        value == 0
            ? throw new ArgumentException("SequenceId cannot be zero.", nameof(value))
            : new SequenceId(value);

    public static implicit operator ulong(SequenceId _) => _.Value;
	public static implicit operator SequenceId(ulong _) => From(_);

	public override string ToString() => Value.ToString();

	#region . relational members .

	public int CompareTo(SequenceId other) => Value.CompareTo(other.Value);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is SequenceId other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(SequenceId)}");
	}

	public static bool operator <(SequenceId left, SequenceId right)  => left.CompareTo(right) < 0;
	public static bool operator >(SequenceId left, SequenceId right)  => left.CompareTo(right) > 0;
	public static bool operator <=(SequenceId left, SequenceId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(SequenceId left, SequenceId right) => left.CompareTo(right) >= 0;

	#endregion
}

public sealed class SequenceIdGenerator(ulong initialValue = 1) {
	// Subtracting one because Interlocked.Increment will return the post-incremented value
	// which is expected to be the initialSequenceId for the first call
	long _actual = unchecked((long)initialValue - 1);

	public SequenceId FetchNext() => unchecked((ulong)Interlocked.Increment(ref _actual));
}

public interface ISequentialIdGenerator {
	ValueTask       EnsureSequenceExists();
	ValueTask<long> Next(CancellationToken ct = default);
	ValueTask<long> Current(CancellationToken ct = default);
	ValueTask       Reset();
}
