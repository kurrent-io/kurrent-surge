using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the expected state of a stream when performing operations such as appending records.
/// Either a named condition (Any, Exists, NoStream, Deleted, Tombstoned) or a concrete <see cref="Revision"/>.
/// Superset of <see cref="ActualStreamState"/>.
/// </summary>
[PublicAPI]
public readonly partial record struct ExpectedStreamState : IVariant<ExpectedStreamCondition, Revision> {
    /// <summary>
    /// No precondition on the stream's existence or revision.
    /// </summary>
    /// <remarks>
    /// The operation succeeds regardless of whether the stream exists, has been deleted, or is at any particular revision.
    /// <para />
    /// Use this when you do not need optimistic concurrency and simply want the operation to proceed unconditionally.
    /// <para />
    /// Convenience alias for <see cref="ExpectedStreamCondition.Any"/>.
    /// </remarks>
    public static readonly ExpectedStreamState Any = ExpectedStreamCondition.Any;

    /// <summary>
    /// The stream must already exist with at least one committed record.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has never been written to or is in the NoStream state.
    /// <para />
    /// Unlike specifying a concrete <see cref="Revision"/>, this does not check the exact revision -
    /// only that the stream is non-empty.
    /// <para />
    /// Convenience alias for <see cref="ExpectedStreamCondition.Exists"/>.
    /// </remarks>
    public static readonly ExpectedStreamState Exists = ExpectedStreamCondition.Exists;

    /// <summary>
    /// The stream must not exist.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream already has any committed records.
    /// <para />
    /// Use this to guarantee that the operation creates a new stream rather than appending to an existing one.
    /// <para />
    /// Convenience alias for <see cref="ExpectedStreamCondition.NoStream"/>.
    /// </remarks>
    public static readonly ExpectedStreamState NoStream = ExpectedStreamCondition.NoStream;

    /// <summary>
    /// The stream must be in a soft-deleted state.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has not been soft-deleted.
    /// <para />
    /// A soft-deleted stream can be recreated; its revision is preserved and continues to increment for new records.
    /// <para />
    /// Convenience alias for <see cref="ExpectedStreamCondition.Deleted"/>.
    /// </remarks>
    public static readonly ExpectedStreamState Deleted = ExpectedStreamCondition.Deleted;

    /// <summary>
    /// The stream must be in a tombstoned state.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has not been hard-deleted and tombstoned.
    /// <para />
    /// A tombstoned stream cannot be recreated or have any new records appended to it.
    /// <para />
    /// Convenience alias for <see cref="ExpectedStreamCondition.Tombstoned"/>.
    /// </remarks>
    public static readonly ExpectedStreamState Tombstoned = ExpectedStreamCondition.Tombstoned;

    internal static ExpectedStreamState FromWireValue(long value) =>
        value switch {
            >= 0 => Revision.From(value),
            -1   => NoStream,
            -2   => Any,
            -4   => Exists,
            -5   => Deleted,
            -6   => Tombstoned,
            _    => throw new ArgumentOutOfRangeException(nameof(value), value, $"Cannot convert value {value}. Expected -1 (NoStream), -2 (Any), -4 (Exists), -5 (Deleted), or -6 (Tombstoned).")
        };

    internal long ToWireValue() =>
        Match(condition => (long)condition, revision => revision.Value);

    /// <summary>
    /// Cross-DU conversion: an actual stream state is always a valid expectation.
    /// </summary>
    public static implicit operator ExpectedStreamState(ActualStreamState state) =>
        state.Match<ExpectedStreamState>(condition => (ExpectedStreamCondition)condition, revision => revision);
}

/// <summary>
/// The expected state of a stream as a named precondition. Not a concrete revision.
/// Superset of <see cref="ActualStreamCondition"/> — includes the expectation-only values Any and Exists.
/// </summary>
[PublicAPI]
public enum ExpectedStreamCondition : long {
    /// <summary>
    /// No precondition on the stream's existence or revision.
    /// </summary>
    /// <remarks>
    /// The operation succeeds regardless of whether the stream exists, has been deleted, or is at any particular revision.
    /// <para />
    /// Use this when you do not need optimistic concurrency and simply want the operation to proceed unconditionally.
    /// </remarks>
    Any = -2,

    /// <summary>
    /// The stream must already exist with at least one committed record.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has never been written to or is in the NoStream state.
    /// <para />
    /// Unlike specifying a concrete revision, this does not check the exact revision —
    /// only that the stream is non-empty.
    /// </remarks>
    Exists = -4,

    /// <summary>
    /// The stream must not exist.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream already has any committed records.
    /// <para />
    /// Use this to guarantee that the operation creates a new stream rather than appending to an existing one.
    /// </remarks>
    NoStream = -1,

    /// <summary>
    /// The stream must be in a soft-deleted state.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has not been soft-deleted.
    /// <para />
    /// A soft-deleted stream can be recreated; its revision is preserved and continues to increment for new records.
    /// </remarks>
    Deleted = -5,

    /// <summary>
    /// The stream must be in a tombstoned state.
    /// </summary>
    /// <remarks>
    /// The operation fails if the stream has not been hard-deleted and tombstoned.
    /// <para />
    /// A tombstoned stream cannot be recreated or have any new records appended to it.
    /// </remarks>
    Tombstoned = -6,
}

/// <summary>
/// Represents the actual state of a stream as reported by the server.
/// Either a named condition (NoStream, Deleted, Tombstoned) or a concrete <see cref="Revision"/>.
/// </summary>
[PublicAPI]
public readonly partial record struct ActualStreamState : IVariant<ActualStreamCondition, Revision> {
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
        >= 0 => Revision.From(value),
        -1   => ActualStreamCondition.NoStream,
        -5   => ActualStreamCondition.Deleted,
        -6   => ActualStreamCondition.Tombstoned,
        _    => throw new ArgumentOutOfRangeException(nameof(value), value, $"Cannot convert value {value}. Expected a revision or -1 (NoStream), -5 (Deleted), or -6 (Tombstoned).")
    };

    internal long ToWireValue() =>
        Match(condition => (long)condition, revision => revision.Value);
}

/// <summary>
/// The actual state of a stream. Not a concrete revision.
/// </summary>
[PublicAPI]
public enum ActualStreamCondition : long {
    /// <summary>
    /// The stream does not exist.
    /// </summary>
    /// <remarks>
    /// The stream has never been created or appended to, or it has been soft-deleted and then recreated, resulting in a positive revision number.
    /// <para />
    /// In either case, the server reports the state as NoStream when the stream is currently empty and has no committed records.
    /// </remarks>
    NoStream = -1,

    /// <summary>
    /// The stream has been soft-deleted.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream, however the stream may still be recreated with the same name and new records appended to it.
    /// <para />
    /// Its revision was not reset and will continue to increment for new records.
    /// </remarks>
    Deleted = -5,

    /// <summary>
    /// The stream has been hard-deleted and tombstoned.
    /// </summary>
    /// <remarks>
    /// All records have been removed from the stream and it is no longer accessible.
    /// <para />
    /// It cannot be recreated or have any new records appended to it.
    /// </remarks>
    Tombstoned = -6,
}
