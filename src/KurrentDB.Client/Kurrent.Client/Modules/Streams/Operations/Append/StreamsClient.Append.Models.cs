#pragma warning disable CS8604 // Possible null reference argument.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamRequest(string Stream, ExpectedStreamState ExpectedState, IEnumerable<AppendRecord> Messages) {
    public required string                    Stream        { get; init; } = Stream;
    public required IEnumerable<AppendRecord> Messages      { get; init; } = Messages;
    public required ExpectedStreamState       ExpectedState { get; init; } = ExpectedState;

    public static AppendStreamRequestBuilder New() => new();
}

[PublicAPI]
[method: SetsRequiredMembers]
public record AppendStreamSuccess(string Stream, LogPosition Position, StreamRevision StreamRevision) {
    public required string         Stream         { get; init; } = Stream;
    public required LogPosition    Position       { get; init; } = Position;
    public required StreamRevision StreamRevision { get; init; } = StreamRevision;
}

[PublicAPI]
public record AppendSessionSuccess(IReadOnlyList<AppendStreamSuccess> Results, LogPosition Position) : IReadOnlyList<AppendStreamSuccess> {
    public int Count => Results.Count;

    public AppendStreamSuccess this[int index] => Results[index];

    public IEnumerator<AppendStreamSuccess> GetEnumerator() => Results.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[PublicAPI]
public partial class AppendSessionError : IVariantException<
    StreamNotFoundException,
    StreamAlreadyExistsException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    StreamAlreadyInAppendSessionException,
    MaxAppendRecordSizeExceededException,
    MaxAppendTransactionSizeExceededException,
    AppendSessionNoRequestsException,
    Exception> {
    public static AppendSessionError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<AppendSessionError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamAlreadyExistsException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e),
            static (s, e) => StreamAlreadyInAppendSessionException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e),
            static (s, e) => AppendSessionNoRequestsException.From(s, e));
}

[PublicAPI]
public record AppendRecord {
    public static readonly AppendRecord None = new() {
        RecordId   = Guid.Empty,
        Value      = null!,
        DataFormat = SchemaDataFormat.Unspecified,
        Metadata   = null!
    };

    public static AppendRecordBuilder New => new();

    /// <summary>
    /// The assigned record id.
    /// </summary>
    public Guid RecordId { get; internal init; } = Guid.NewGuid();

    /// <summary>
    /// The message payload.
    /// </summary>
    public object Value { get; internal init; } = null!;

    /// <summary>
    /// Specifies the format of the schema associated with the message.
    /// </summary>
    public SchemaDataFormat DataFormat { get; internal init; } = SchemaDataFormat.Json;

    /// <summary>
    /// Optional metadata associated with the record.
    /// </summary>
    public Metadata Metadata { get; internal init; } = new();

    /// <summary>
    /// Creates a new <see cref="AppendRecord"/> instance with the specified value and data format.
    /// </summary>
    /// <param name="value">
    /// The content value to include in the message. This cannot be null and must align with the specified
    /// <paramref name="dataFormat"/> (e.g., when passing byte arrays, the format must be <see cref="SchemaDataFormat.Bytes"/>).
    /// </param>
    /// <param name="dataFormat">
    /// The format of the data represented by <paramref name="value"/>. Defaults to <see cref="SchemaDataFormat.Json"/>.
    /// Cannot be <see cref="SchemaDataFormat.Unspecified"/>.
    /// </param>
    public static AppendRecord Create(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) =>
        New.WithValue(value).WithDataFormat(dataFormat).Build();
}

/// <summary>
/// Represents the expected state of a stream when performing operations such as appending messages.
/// </summary>
public readonly record struct ExpectedStreamState {
    /// <summary>
    /// The stream should not exist.
    /// </summary>
    public static readonly ExpectedStreamState NoStream = new(-1);

    /// <summary>
    /// The stream may or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState Any = new(-2);

    /// <summary>
    /// The stream must exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamExists = new(-4);

    public long Value { get; }

    internal ExpectedStreamState(long value) {
        Value = value switch {
            -1 or -2 or -4 or >= 0 => value,
            -1000                  => -2, // Special case for unset revision to Any
            _ => throw new ArgumentOutOfRangeException(
                nameof(value), value, "ExpectedStreamState must be NoStream(-1), Any(-2), StreamExists(-4), or a StreamRevision(0+)")
        };
    }

    public static implicit operator ExpectedStreamState(long _)           => new(_);
    public static implicit operator long(ExpectedStreamState _)           => _.Value;
    public static implicit operator StreamRevision(ExpectedStreamState _) => _.Value;
    public static implicit operator ulong(ExpectedStreamState _)          => (ulong)_.Value;
}
