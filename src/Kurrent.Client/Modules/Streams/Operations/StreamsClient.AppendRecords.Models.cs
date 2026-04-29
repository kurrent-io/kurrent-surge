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
    public static Builder New() =>
        new();

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
    public ImmutableArray<AppendRecord> Records {
        get; init { field = value; ResetValidation(); }
    } = [];

    /// <summary>
    /// Gets the consistency checks to be performed before appending.
    /// </summary>
    public ImmutableArray<ConsistencyCheck> Checks {
        get; init { field = value; ResetValidation(); }
    } = [];

    /// <summary>
    /// Validates all records meet their invariants and have a stream assigned.
    /// </summary>
    public new AppendRecordsRequest EnsureValid() {
        Validate().EnsureValid();
        return this;
    }

    protected override ValidationResult RunValidation() {
        foreach (var record in Records) {
            if (record.Stream == StreamName.Unset)
                return ValidationResult.FromProblem(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordStreamRequired);

            var result = record.Validate();

            if (result.IsInvalid)
                return result;
        }

        return Results.Success;
    }

    /// <summary>
    /// Builds an <see cref="AppendRecordsRequest"/> where each record carries its own stream assignment.
    /// </summary>
    public class Builder(AppendRecordsRequest request) : FluentBuilder<Builder, AppendRecordsRequest>(request) {
        public Builder() : this(new AppendRecordsRequest()) { }

        /// <inheritdoc />
        protected override AppendRecordsRequest OnBuild(AppendRecordsRequest request) {
            if (request.Records.Length == 0)
                throw new ValidationException(
                    ErrorCode.FromType<AppendRecordsRequest>(),
                    ErrorMessages.AppendRecordsRequestEmpty,
                    ValidationProblem.Create(ErrorCode.FromType<AppendRecordsRequest>(), ErrorMessages.AppendRecordsRequestEmpty));

            foreach (var record in request.Records) {
                if (record.Stream == StreamName.Unset)
                    throw new ValidationException(
                        ErrorCode.FromType<AppendRecordsRequest>(),
                        ErrorMessages.AppendRecordStreamRequired,
                        ValidationProblem.Create(ErrorCode.FromType<AppendRecordsRequest>(), ErrorMessages.AppendRecordStreamRequired));
            }

            return request;
        }

        #region ->> Consistency Checks <<-

        public Builder WithCheck(ConsistencyCheck check) =>
            With(r => r with { Checks = r.Checks.Add(check) });

        public Builder WithChecks(params ConsistencyCheck[] checks) =>
            With(r => r with { Checks = r.Checks.AddRange(checks) });

        #endregion

        #region ->> Records <<-

        public Builder WithRecord(AppendRecord record) =>
            With(r => r with { Records = r.Records.Add(record) });

        public Builder WithRecord(AppendRecord.Builder builder) =>
            With(r => r with { Records = r.Records.Add(builder.Build()) });

        public Builder WithRecord(Action<AppendRecord.Builder> configure) =>
            With(r => {
                var builder = new AppendRecord.Builder();
                configure(builder);
                return r with { Records = r.Records.Add(builder.Build()) };
            });

        public Builder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json) =>
            With(r => r with { Records = r.Records.Add(AppendRecord.New.WithValue(value).WithDataFormat(dataFormat).Build()) });

        public Builder WithRecord<T>(T value, StreamName stream, SchemaDataFormat dataFormat = SchemaDataFormat.Json) =>
            With(r => r with { Records = r.Records.Add(AppendRecord.New.ForStream(stream).WithValue(value).WithDataFormat(dataFormat).Build()) });

        public Builder WithRecords(params AppendRecord[] records) =>
            With(r => r with { Records = r.Records.AddRange(records) });

        public Builder WithRecords(IEnumerable<AppendRecord> records) =>
            With(r => r with { Records = r.Records.AddRange(records) });

        #endregion

        #region ->> Batch Overrides <<-

        public Builder WithDataFormat(SchemaDataFormat dataFormat) =>
            WithOverride(r => r with { Records = r.Records.Select(rec => rec with { DataFormat = dataFormat }).ToImmutableArray() });

        public Builder WithMetadata(string key, object value) =>
            WithOverride(r => r with { Records = r.Records.Select(rec => rec with { Metadata = rec.Metadata.With(key, value) }).ToImmutableArray() });

        #endregion
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
        new(stream, revision);

    public static StreamStateCheck StreamExists(StreamName stream) =>
        new(stream, ExpectedStreamState.Exists);

    public static StreamStateCheck StreamDoesNotExist(StreamName stream) =>
        new(stream, ExpectedStreamState.NoStream);

    public static StreamStateCheck StreamIsDeleted(StreamName stream) =>
        new(stream, ExpectedStreamState.Deleted);

    public static StreamStateCheck StreamIsTombstoned(StreamName stream) =>
        new(stream, ExpectedStreamState.Tombstoned);
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