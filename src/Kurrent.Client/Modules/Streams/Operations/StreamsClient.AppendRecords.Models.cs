#pragma warning disable CS8604 // Possible null reference argument.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Validation;
using Kurrent.Variant;
using Resources;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents a request to append multiple records to the system.
/// </summary>
/// <remarks>
/// This request type allows atomically appending multiple records across different streams,
/// with optional consistency checks to ensure data integrity.
/// </remarks>
[PublicAPI]
public record AppendRecordsRequest() : SelfValidating {
    /// <summary>
    /// Creates a builder for appending records across one or more streams,
    /// with optional consistency checks.
    /// </summary>
    public static Builder New() => new();

    [method: SetsRequiredMembers]
    public AppendRecordsRequest(
        ImmutableArray<AppendRecord> records,
        ImmutableArray<ConsistencyCheck> checks
    ) : this() {
        Records = records;
        Checks  = checks;
    }

    /// <summary>
    /// Gets the records to be appended.
    /// </summary>
    public required ImmutableArray<AppendRecord> Records {
        get;
        init {
            field = value;
            ResetValidation();
        }
    }

    /// <summary>
    /// Gets the consistency checks to be performed before appending.
    /// </summary>
    public required ImmutableArray<ConsistencyCheck> Checks {
        get;
        init {
            field = value;
            ResetValidation();
        }
    }

    /// <summary>
    /// Validates all records meet their invariants and have a stream assigned.
    /// </summary>
    public new AppendRecordsRequest EnsureValid() {
        Validate().Unwrap();
        return this;
    }

    public override ValidationResult RunValidation() {
        foreach (var record in Records) {
            if (record.Stream == StreamName.Unset)
                return ValidationError.FromProblem(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordStreamRequired);

            var result = record.Validate();
            if (!result.IsSuccess)
                return result;
        }

        return Results.Success;
    }
    
    /// <summary>
    /// Builds an <see cref="AppendRecordsRequest"/> where each record carries its own stream assignment.
    /// </summary>
    public class Builder {
        readonly List<AppendRecord.Builder>  _builders         = [];
        readonly List<ConsistencyCheck>     _checks            = [];
        readonly Dictionary<string, object> _metadataOverrides = [];

        SchemaDataFormat? _dataFormatOverride;

        internal Builder() { }

        public int RecordsCount => _builders.Count;

        #region ->> Consistency Checks <<-

        public Builder WithCheck(ConsistencyCheck check) {
            _checks.Add(check);
            return this;
        }

        public Builder WithChecks(params ConsistencyCheck[] checks) {
            foreach (var check in checks)
                _checks.Add(check);
            return this;
        }

        #endregion

        #region ->> Records <<-

        public Builder WithRecord(AppendRecord record) {
            _builders.Add(new AppendRecord.Builder().FromRecord(record));
            return this;
        }

        public Builder WithRecord(AppendRecord.Builder builder) {
            _builders.Add(builder);
            return this;
        }

        public Builder WithRecord(Action<AppendRecord.Builder> configure) {
            var builder = new AppendRecord.Builder();
            configure(builder);
            _builders.Add(builder);
            return this;
        }

        public Builder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
            _builders.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat));
            return this;
        }

        public Builder WithRecord<T>(T value, StreamName stream, SchemaDataFormat dataFormat = SchemaDataFormat.Json) {
            _builders.Add(AppendRecord.New.ForStream(stream).WithValue(value).WithDataFormat(dataFormat));
            return this;
        }

        public Builder WithRecords(params AppendRecord[] records) {
            foreach (var record in records)
                _builders.Add(new AppendRecord.Builder().FromRecord(record));
            return this;
        }

        public Builder WithRecords(IEnumerable<AppendRecord> records) {
            foreach (var record in records)
                _builders.Add(new AppendRecord.Builder().FromRecord(record));
            return this;
        }

        #endregion

        #region Batch Overrides

        public Builder WithDataFormat(SchemaDataFormat dataFormat) {
            _dataFormatOverride = dataFormat;
            return this;
        }

        public Builder WithMetadata(string key, object value) {
            _metadataOverrides[key] = value;
            return this;
        }

        #endregion

        public AppendRecordsRequest Build() {
            if (_builders.Count == 0)
                throw ValidationError.FromProblem(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordsRequestEmpty
                ).GetOrCreateException();

            var records = ImmutableArray.CreateBuilder<AppendRecord>(_builders.Count);

            foreach (var builder in _builders) {
                if (_dataFormatOverride.HasValue)
                    builder.WithDataFormat(_dataFormatOverride.Value);

                foreach (var (key, value) in _metadataOverrides)
                    builder.WithMetadata(key, value);

                var record = builder.Build();

                if (record.Stream == StreamName.Unset)
                    throw ValidationError.FromProblem(
                        ErrorCode.FromType<AppendRecordsRequest>(),
                        ErrorMessages.AppendRecordStreamRequired
                    ).GetOrCreateException();

                records.Add(record);
            }

            return new AppendRecordsRequest(records.MoveToImmutable(), [.._checks]);
        }
    }
}

/// <summary>
/// The result of appending records across multiple streams.
/// </summary>
[PublicAPI]
[method: SetsRequiredMembers]
public record AppendRecordsResponse(StreamPosition[] Revisions, LogPosition Position) {
    public required StreamPosition[] Revisions { get; init; } = Revisions;
    public required LogPosition      Position  { get; init; } = Position;
}

/// <summary>
/// A pre-commit condition that must hold true for the append transaction to succeed.
/// Can be either a <see cref="StreamStateCheck"/> (stream must be at a specific state)
/// or a <see cref="QueryPredicateCheck"/> (a query predicate must evaluate to true).
/// </summary>
public readonly partial record struct ConsistencyCheck : IVariant<StreamStateCheck, QueryPredicateCheck> {
    public static readonly ConsistencyCheck None = new();

    public static StreamStateCheck StreamAtRevision(StreamName stream, Revision revision) =>
        new StreamStateCheck(stream, revision);

    public static StreamStateCheck StreamExists(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Exists);

    public static StreamStateCheck StreamDoesNotExist(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.NoStream);

    public static StreamStateCheck StreamIsDeleted(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Deleted);

    public static StreamStateCheck StreamIsTombstoned(StreamName stream) =>
        new StreamStateCheck(stream, ExpectedStreamState.Tombstoned);
}

/// <summary>
/// Asserts a stream is at a specific revision or lifecycle state before commit.
/// </summary>
public readonly record struct StreamStateCheck(StreamName Stream, ExpectedStreamState ExpectedState);

/// <summary>
/// A query predicate evaluated before commit.
/// The transaction is aborted if the predicate expression evaluates to false.
/// </summary>
public readonly record struct QueryPredicateCheck(string Expression) {
    /// <summary>
    /// Gets the predicate expression to evaluate.
    /// </summary>
    public required string Expression { get; init; } = Expression;
}

