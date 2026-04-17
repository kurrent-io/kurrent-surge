namespace Kurrent.Client.Streams.Checkpoints;

/// <summary>
/// Represents a unique identifier used for sequencing items.
/// </summary>
/// <remarks>
/// <para>
/// SequenceId has a static value <see cref="Unset"/> which represents a default sequence identifier with a value of 0.
/// Using this as a default ensures that invalid identifiers can be distinguished from valid values.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct SequenceId : IComparable<SequenceId>, IComparable {
	public static readonly SequenceId Unset = new(0);

	public bool IsUnset => this == Unset;

	SequenceId(ulong value) => Value = value;

	public ulong Value { get; }

	public static SequenceId From(ulong value) =>
        value == 0
            ? throw new ArgumentException("SequenceId cannot be zero.", nameof(value))
            : new SequenceId(value);

    public static implicit operator ulong(SequenceId _) => _.Value;
	public static implicit operator SequenceId(ulong _) => From(_);

	public override string ToString() => Value.ToString();

	#region ->> relational members <<-

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

public sealed class SequenceGenerator(ulong initialValue = 1) {
	// Subtracting one because Interlocked.Increment will return the post-incremented value
	// which is expected to be the initialSequenceId for the first call
	long _actual = unchecked((long)initialValue - 1);

	public SequenceId FetchNext() => unchecked((ulong)Interlocked.Increment(ref _actual));
}

//
// /// <summary>
// /// Represents a unique identifier used for sequencing items.
// /// </summary>
// /// <remarks>
// /// <para>
// /// SequenceId has a static value <see cref="Unset"/> which represents a default sequence identifier with a value of 0.
// /// Using this as a default ensures that invalid identifiers can be distinguished from valid values.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public readonly record struct Sequence : IComparable<Sequence>, IComparable {
// 	public static readonly Sequence None = new(0);
//
//     public static SequenceGenerator Generator(ulong initialValue = 1) => new(initialValue);
//
//     Sequence(ulong value) => Value = value;
//
// 	public ulong Value { get; }
//
// 	public static Sequence From(ulong value) =>
//         value == 0
//             ? throw new ArgumentException("Sequence cannot be zero.", nameof(value))
//             : new Sequence(value);
//
//     public static implicit operator ulong(Sequence _) => _.Value;
// 	public static implicit operator Sequence(ulong _) => From(_);
//
// 	public override string ToString() => Value.ToString();
//
// 	#region . relational members .
//
// 	public int CompareTo(Sequence other) => Value.CompareTo(other.Value);
//
// 	public int CompareTo(object? obj) {
// 		if (ReferenceEquals(null, obj))
// 			return 1;
//
// 		return obj is Sequence other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(Sequence)}");
// 	}
//
// 	public static bool operator <(Sequence left, Sequence right)  => left.CompareTo(right) < 0;
// 	public static bool operator >(Sequence left, Sequence right)  => left.CompareTo(right) > 0;
// 	public static bool operator <=(Sequence left, Sequence right) => left.CompareTo(right) <= 0;
// 	public static bool operator >=(Sequence left, Sequence right) => left.CompareTo(right) >= 0;
//
// 	#endregion
//
//     class SequenceGenerator(ulong initialValue = 1) {
//         // Subtracting one because Interlocked.Increment will return the post-incremented value
//         // which is expected to be the initialSequenceId for the first call
//         long _actual = unchecked((long)initialValue - 1);
//
//         public SequenceId FetchNext() => unchecked((ulong)Interlocked.Increment(ref _actual));
//     }
// }
