using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;
using Resources;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append records to a single stream.
/// All records are assigned to the specified stream regardless of their individual Stream property.
/// </summary>
[PublicAPI]
public record AppendStreamRequest() : SelfValidating {
    /// <summary>
    /// Creates a builder for appending records to the specified stream.
    /// </summary>
    public static AppendStreamRequestBuilder For(StreamName stream) => new(stream);

    [method: SetsRequiredMembers]
    public AppendStreamRequest(
        StreamName stream,
        ImmutableArray<AppendRecord> records,
        ExpectedStreamState expectedState
    ) : this() {
        Stream        = stream;
        Records       = records;
        ExpectedState = expectedState;
    }

    /// <summary>
    /// The target stream for all records.
    /// </summary>
    public required StreamName Stream {
        get;
        init {
            field = value;
            ResetValidation();
        }
    }

    /// <summary>
    /// The records to append. All will be assigned to <see cref="Stream"/>.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records {
        get;
        init {
            field = value;
            ResetValidation();
        }
    }

    /// <summary>
    /// The expected state of the stream before appending.
    /// </summary>
    public required ExpectedStreamState ExpectedState {
        get;
        init {
            field = value;
            ResetValidation();
        }
    }

    /// <summary>
    /// Validates all records meet their invariants and stamps the target stream on each.
    /// Returns a new request with the validated, stream-stamped records.
    /// </summary>
    public new AppendStreamRequest EnsureValid() {
        Validate().Unwrap();

        var builder        = Records.ToBuilder();
        var recordsChanged = false;

        for (var i = 0; i < builder.Count; i++) {
            var record = builder[i];

            if (record.Stream == Stream)
                continue;

            builder[i]     = record with { Stream = Stream };
            recordsChanged = true;
        }

        if (recordsChanged)
            return this with { Records = builder.MoveToImmutable() };

        return this;
    }

    public override ValidationResult RunValidation() {
        if (Stream == StreamName.Unset)
            return ValidationError.FromProblem(
                ErrorCode.FromType<AppendStreamRequest>(),
                ErrorMessages.AppendRecordStreamRequired);

        foreach (var record in Records) {
            var result = record.Validate();
            if (!result.IsSuccess)
                return result;
        }

        return Results.Success;
    }
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
// [method: SetsRequiredMembers]
public record AppendStreamResponse(StreamName Stream, Revision Revision, LogPosition Position) {
    // public required StreamName  Stream   { get; init; } = Stream;
    // public required Revision    Revision { get; init; } = Revision;
    // public required LogPosition Position { get; init; } = Position;
}

/// <summary>
/// The result of appending records to a single stream.
/// </summary>
[PublicAPI]
// [method: SetsRequiredMembers]
public record AppendStreamResponse4(LogPosition Position, Revision Revision) {
    // public required Revision    Revision { get; init; } = Revision;
    // public required LogPosition Position { get; init; } = Position;
}

public record AppendStreamResponse2(StreamPosition StreamPosition, LogPosition LogPosition) {
    public static AppendStreamResponse2 Unset { get; } = new(StreamPosition.Unset, LogPosition.Unset);
}

public record AppendStreamResponse3(RecordPosition Position) {
    public static AppendStreamResponse3 Unset { get; } = new(RecordPosition.Unset);
}

/// <summary>
/// Builds an <see cref="AppendStreamRequest"/> where all records target a single stream.
/// </summary>
[PublicAPI]
public class AppendStreamRequestBuilder {
    readonly StreamName                _stream;
    readonly List<AppendRecordBuilder> _builders = [];

    ExpectedStreamState _expectedState = ExpectedStreamState.Any;

    internal AppendStreamRequestBuilder(StreamName stream) => _stream = stream;

    public int RecordsCount => _builders.Count;

    public AppendStreamRequestBuilder ExpectingState(ExpectedStreamState expectedState) {
        if (expectedState.Value is Revision revision && revision == Revision.Unset)
            return this;

        if (expectedState == ExpectedStreamState.Any)
            return this;

        _expectedState = expectedState;
        return this;
    }

    #region ->> Records <<-

    public AppendStreamRequestBuilder WithRecord(AppendRecord record) {
        _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> build) {
        var builder = AppendRecord.New;
        build(builder);
        _builders.Add(builder.ForStream(_stream));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord<T>(T value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull {
        _builders.Add(AppendRecord.New.ForStream(_stream).WithValue(value).WithDataFormat(dataFormat));
        return this;
    }

    public AppendStreamRequestBuilder WithRecord<T>(T value, Metadata metadata, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull {
        _builders.Add(AppendRecord.New.ForStream(_stream).WithValue(value).WithMetadata(metadata).WithDataFormat(dataFormat));
        return this;
    }

    public AppendStreamRequestBuilder WithRecords(IEnumerable<AppendRecord> records) {
        foreach (var record in records)
            _builders.Add(AppendRecord.New.FromRecord(record).ForStream(_stream));
        return this;
    }

    #endregion

    public AppendStreamRequest Build() {
        if (_builders.Count == 0)
            throw ValidationError.FromProblem(
                ErrorCode.FromType<AppendStreamRequest>(),
                ErrorMessages.AppendRecordsRequestEmpty
            ).GetOrCreateException();

        var records = ImmutableArray.CreateRange(_builders.Select(b => b.Build()));

        return new AppendStreamRequest(_stream, records, _expectedState);
    }
}

