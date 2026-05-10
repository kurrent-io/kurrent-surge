using System.Collections.Immutable;
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
    public static Builder For(StreamName stream) => new(stream);

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
    public StreamName Stream {
        get;
        init {
            field = value;
            ResetValidation();
        }
    } = StreamName.Unset;

    /// <summary>
    /// The records to append. All will be assigned to <see cref="Stream"/>.
    /// </summary>
    public ImmutableArray<AppendRecord> Records {
        get;
        init {
            field = value;
            ResetValidation();
        }
    } = [];

    /// <summary>
    /// The expected state of the stream before appending.
    /// </summary>
    public ExpectedStreamState ExpectedState {
        get;
        init {
            field = value;
            ResetValidation();
        }
    } = ExpectedStreamState.Any;

    /// <summary>
    /// Validates all records meet their invariants and stamps the target stream on each.
    /// Returns a new request with the validated, stream-stamped records.
    /// </summary>
    public new AppendStreamRequest EnsureValid() {
        Validate().EnsureValid();

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

    protected override ValidationResult RunValidation() {
        if (Stream == StreamName.Unset)
            return ValidationResult.FromProblem(
                ErrorCode.FromType<AppendStreamRequest>(),
                ErrorMessages.AppendRecordStreamRequired);

        foreach (var record in Records) {
            var result = record.Validate();
            if (result.IsInvalid)
                return result;
        }

        return Results.Success;
    }
    
    /// <summary>
    /// Builds an <see cref="AppendStreamRequest"/> where all records target a single stream.
    /// </summary>
    public class Builder(AppendStreamRequest request) : FluentBuilder<Builder, AppendStreamRequest>(request) {
        public Builder() : this(new AppendStreamRequest()) { }
        public Builder(StreamName stream) : this(new AppendStreamRequest { Stream = stream }) { }

        /// <inheritdoc />
        protected override AppendStreamRequest OnBuild(AppendStreamRequest request) {
            if (request.Records.Length == 0)
                throw new ValidationException(
                    ErrorCode.FromType<AppendStreamRequest>(),
                    ErrorMessages.AppendRecordsRequestEmpty,
                    ValidationProblem.Create(ErrorCode.FromType<AppendStreamRequest>(), ErrorMessages.AppendRecordsRequestEmpty));

            return request;
        }

        public Builder ExpectingState(ExpectedStreamState expectedState) {
            if (expectedState.Value is Revision revision && revision == Revision.Unset)
                return this;

            if (expectedState == ExpectedStreamState.Any)
                return this;

            return With(r => r with { ExpectedState = expectedState });
        }

        #region ->> Records <<-

        public Builder WithRecord(AppendRecord record) =>
            With(r => r with { Records = r.Records.Add(record with { Stream = r.Stream }) });

        public Builder WithRecord(Action<AppendRecord.Builder> build) =>
            With(r => {
                var builder = new AppendRecord.Builder();
                build(builder);
                return r with { Records = r.Records.Add(builder.ForStream(r.Stream).Build()) };
            });

        public Builder WithMessage<T>(T value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull =>
            With(r => r with {
                Records = r.Records.Add(
                    AppendRecord.New.ForStream(r.Stream).WithValue(value).WithDataFormat(dataFormat).Build())
            });

        public Builder WithMessage<T>(T value, Metadata metadata, SchemaDataFormat dataFormat = SchemaDataFormat.Json) where T : notnull =>
            With(r => r with {
                Records = r.Records.Add(
                    AppendRecord.New.ForStream(r.Stream).WithValue(value).WithMetadata(metadata).WithDataFormat(dataFormat).Build())
            });

        public Builder WithRecords(IEnumerable<AppendRecord> records) =>
            With(r => r with {
                Records = r.Records.AddRange(records.Select(rec => rec with { Stream = r.Stream }))
            });

        #endregion
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

