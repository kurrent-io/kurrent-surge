using System.Diagnostics;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
[DebuggerDisplay("{DebuggerDisplayValue}")]
public record Revision : IComparable<Revision>, IComparable {

    public static readonly Revision Unset = new(-1000L);

    /// <summary>
    /// The beginning (i.e., the first event) of a stream.
    /// </summary>
    public static readonly Revision Min = new(0L);

    /// <summary>
    /// The end of a stream. Use this when reading a stream backwards or subscribing live to a stream.
    /// </summary>
    public static readonly Revision Max = new(long.MaxValue);

    Revision(long value) => Value = value;

    public long Value { get; }

    public bool IsUnset => this == Unset;

    public override string ToString() => Value.ToString();

    string DebuggerDisplayValue => this switch {
        _ when this == Unset => $"revision: {Value} (Unset)",
        _ when this == Min   => $"revision: {Value} (Min)",
        _ when this == Max   => $"revision: {Value} (Max)",
        _                    => $"revision: {Value}"
    };

    public static Revision From(long revisionValue) =>
        revisionValue switch {
            -1000 => Unset,
            >= 0  => new(revisionValue),
            _     => throw new InvalidRevision(revisionValue)
        };

    public static Revision Parse(string? revisionValue) =>
        long.TryParse(revisionValue?.Replace("r", "".Trim()), out var revision)
            ? From(revision)
            : throw new InvalidRevisionFormat(revisionValue);

    public static implicit operator long(Revision revision) => revision.Value;

    public static implicit operator Revision(long revision) => From(revision);

    public static implicit operator Revision(string? revision) => Parse(revision);

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

public class InvalidRevision(long value) : KurrentException(
    ErrorCode.FromType<InvalidRevision>(),
    $"Invalid revision value: {value}. Revision must be greater than or equal to 0, or equal to -1000 (for Unset)."
);

public class InvalidRevisionFormat(string? value) : KurrentException(
    ErrorCode.FromType<InvalidRevisionFormat>(),
    $"Invalid revision format: {value}."
);
