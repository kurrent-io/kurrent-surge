using System.Diagnostics;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
[DebuggerDisplay("{ToDebugString()}")]
public record Revision : IComparable<Revision>, IComparable {
    /// <summary>
    /// The end of a stream. Use this when reading a stream backwards or subscribing live to a stream.
    /// </summary>
    public static readonly Revision Max = new(long.MaxValue);

    /// <summary>
    /// The beginning (i.e., the first event) of a stream.
    /// </summary>
    public static readonly Revision Min = new((long)0);

    /// <summary>
    /// A special value that refers to an invalid, unassigned or default revision.
    /// </summary>
    public static readonly Revision Unset = new((long)-1000);

    Revision(long value) => Value = value;

    public long Value { get; private init; }

    public bool IsUnset => this == Unset;

    public static Revision From(long value) =>
        value switch {
            -1000 => Unset,
            >= 0  => new Revision(value),
            _     => throw new InvalidRevision(value)
        };

    public static implicit operator long(Revision revision) => revision.Value;
    public static implicit operator Revision(long revision) => From(revision);

    public override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a debug view.
    /// </summary>
    string ToDebugString() {
        var valueText = Value switch {
            -1000         => "Unset",
            0             => "Min",
            long.MaxValue => "Max",
            _             => Value.ToString()
        };

        return $"Revision: {valueText}";
    }

    #region . relational members .

    public int CompareTo(Revision? other) {
        if (ReferenceEquals(this, other)) return 0;

        return other is null ? 1 : Value.CompareTo(other.Value);
    }

    public int CompareTo(object? obj) {
        if (obj is null) return 1;
        if (ReferenceEquals(this, obj)) return 0;

        return obj is Revision other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Revision)}");
    }

    public static bool operator <(Revision? left, Revision? right)  => Comparer<Revision>.Default.Compare(left, right) < 0;
    public static bool operator >(Revision? left, Revision? right)  => Comparer<Revision>.Default.Compare(left, right) > 0;
    public static bool operator <=(Revision? left, Revision? right) => Comparer<Revision>.Default.Compare(left, right) <= 0;
    public static bool operator >=(Revision? left, Revision? right) => Comparer<Revision>.Default.Compare(left, right) >= 0;

    #endregion
}

public class InvalidRevision(long value)
    : KurrentException(ErrorCode.FromType<InvalidRevision>(), $"Stream revision is invalid: {value}");
