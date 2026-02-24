using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the actual state of a stream as reported by the server.
/// Either a named condition (NoStream, Deleted, Tombstoned) or a concrete <see cref="Revision"/>.
/// </summary>
[PublicAPI]
public readonly partial record struct ActualStreamState
    : IVariant<ActualStreamCondition, Revision> {

    /// <summary>
    /// The stream does not exist.
    /// </summary>
    /// <remarks>
    /// The stream has never been created or appended to, or it has been soft-deleted and then recreated, resulting in a positive revision number.
    /// <para />
    /// In either case, the server reports the state as NoStream when the stream is currently empty and has no committed records.
    /// <para />
    /// Convenience alias for <see cref="ActualStreamCondition.NoStream"/>.
    /// </remarks>
    public static readonly ActualStreamState NoStream = ActualStreamCondition.NoStream;

    /// <summary>
    /// The stream has been soft-deleted.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream, however the stream may still be recreated with the same name and new records appended to it.
    /// <para />
    /// Its revision was not reset and will continue to increment for new records.
    /// <para />
    /// Convenience alias for <see cref="ActualStreamCondition.Deleted"/>.
    /// </remarks>
    public static readonly ActualStreamState Deleted = ActualStreamCondition.Deleted;

    /// <summary>
    /// The stream has been hard-deleted and tombstoned.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream and it is no longer accessible.
    /// <para />
    /// It cannot be recreated or have any new records appended to it.
    /// <para />
    /// Convenience alias for <see cref="ActualStreamCondition.Tombstoned"/>.
    /// </remarks>
    public static readonly ActualStreamState Tombstoned = ActualStreamCondition.Tombstoned;

    internal static ActualStreamState FromWireValue(long value) => value switch {
        >= 0              => Revision.From(value),
        -1 or -10 or -100 => ActualStreamCondition.From(value),
        _                 => throw new ArgumentOutOfRangeException(nameof(value), value, $"Cannot convert value {value}. Expected a stream revision or -1 (NoStream), -10 (Deleted), or -100 (Tombstoned).")
    };

    internal long ToWireValue() =>
        Match(condition => condition.Value, revision => revision.Value);
}

/// <summary>
/// The actual state of a stream. Not a concrete revision.
/// </summary>
[PublicAPI]
public readonly record struct ActualStreamCondition(string Name, long Value) {
    /// <summary>
    /// The stream does not exist.
    /// </summary>
    /// <remarks>
    /// The stream has never been created or appended to, or it has been soft-deleted and then recreated, resulting in a positive revision number.
    /// <para />
    /// In either case, the server reports the state as NoStream when the stream is currently empty and has no committed records.
    /// </remarks>
    public static readonly ActualStreamCondition NoStream = new(nameof(NoStream), -1);

    /// <summary>
    /// The stream has been soft-deleted.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream, however the stream may still be recreated with the same name and new records appended to it.
    /// <para />
    /// Its revision was not reset and will continue to increment for new records.
    /// <para />
    /// </remarks>
    public static readonly ActualStreamCondition Deleted = new(nameof(Deleted), -10);

    /// <summary>
    /// The stream has been hard-deleted and tombstoned.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream and it is no longer accessible.
    /// <para />
    /// It cannot be recreated or have any new records appended to it.
    /// </remarks>
    public static readonly ActualStreamCondition Tombstoned = new(nameof(Tombstoned), -100);

    public override string ToString() => Name;

    public static ActualStreamCondition From(long value) => value switch {
        -1   => NoStream,
        -10  => Deleted,
        -100 => Tombstoned,
        _    => throw new ArgumentOutOfRangeException(nameof(value), value, $"Cannot convert value {value}. Expected -1 (NoStream), -10 (Deleted), or -100 (Tombstoned).")
    };

    /// <summary>
    /// Cross-DU: an actual stream condition is always a valid expected stream state.
    /// </summary>
    public static implicit operator ExpectedStreamState(ActualStreamCondition condition) =>
        new ExpectedStreamCondition(condition.Name, condition.Value);
}
