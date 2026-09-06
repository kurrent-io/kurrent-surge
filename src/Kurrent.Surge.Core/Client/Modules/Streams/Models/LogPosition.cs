// ReSharper disable ConvertIfStatementToReturnStatement
// ReSharper disable DuplicatedSequentialIfBodies

using System.Diagnostics;
using System.Text.RegularExpressions;
using Kurrent.Surge.Errors;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Record position in the global log.
/// </summary>
[PublicAPI]
[DebuggerDisplay("{DebuggerDisplayValue}")]
public readonly partial record struct LogPosition : IComparable<LogPosition>, IComparable {
    /// <summary>
    /// Indicates that the log position is not set and therefore is invalid and should be ignored.
    /// Represented by a value of -1000.
    /// </summary>
    public static readonly LogPosition Unset = new(-1000);

    /// <summary>
    /// Initial position in the transaction file that is used when no other position is specified.
    /// The protocol requires null (not zero) to subscribe from the beginning of the log.
    /// </summary>
    public static readonly LogPosition Earliest = new(-1);

    /// <summary>
    /// Represents the end of the log. Use this when subscribing live to the log, or when reading the log backwards.
    /// </summary>
    public static readonly LogPosition Latest = new(long.MaxValue);

    LogPosition(long value) => Value = value;

    /// <summary>
    /// Represents the underlying value of the log position in the global transaction log.
    /// </summary>
    public long Value { get; } = -1000;

    /// <summary>
    /// Indicates whether this position is <see cref="Unset"/>.
    /// </summary>
    public bool IsUnset => this == Unset;

    public override string ToString() => Value.ToString();

    string DebuggerDisplayValue => this switch {
        _ when this == Unset    => $"{Value} (Unset)",
        _ when this == Earliest => $"{Value} (Earliest)",
        _ when this == Latest   => $"{Value} (Latest)",
        _                       => $"{Value}"
    };

    public static LogPosition From(long? positionValue) =>
        positionValue switch {
            null or -1 => Earliest, -1000 => Unset,
            >= 0       => new(positionValue.Value),
            _          => throw new InvalidLogPosition(positionValue)
        };

    public static LogPosition From(ulong? positionValue) =>
        From((long?)positionValue);

    public static implicit operator long(LogPosition position) => position.Value;

    public static implicit operator LogPosition(long position) => From(position);

    public static implicit operator ulong(LogPosition position) => (ulong)position.Value;

    [GeneratedRegex(@"^C:(?<commit>[0-9]*)\/P:(?<prepare>[0-9]*)$")]
    public static partial Regex LogPositionRegex();

    #region ->> relational members <<-

    public int CompareTo(LogPosition other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) =>
        obj switch {
            null              => 1,
            LogPosition other => CompareTo(other),
            _                 => throw new ArgumentException($"Object must be of type {nameof(LogPosition)}")
        };

    public static bool operator <(LogPosition left, LogPosition right)  => left.CompareTo(right) < 0;
    public static bool operator >(LogPosition left, LogPosition right)  => left.CompareTo(right) > 0;
    public static bool operator <=(LogPosition left, LogPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(LogPosition left, LogPosition right) => left.CompareTo(right) >= 0;

    #endregion
}

public class InvalidLogPosition(long? value) : KurrentException(
    ErrorCode.FromType<InvalidLogPosition>(),
    $"Invalid log position value: {value}. Position must be greater than or equal to -1, or equal to -1000 (for Unset)."
);
