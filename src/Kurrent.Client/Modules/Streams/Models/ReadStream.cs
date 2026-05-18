using Kurrent.Validation;

namespace Kurrent.Client.Streams;

// /// <summary>
// /// Static entry point for building named stream read requests.
// /// </summary>
// [PublicAPI]
// public static class ReadStream {
//     /// <summary>
//     /// Start building read request for the specified stream.
//     /// </summary>
//     public static ReadStreamRequest.Builder Name(StreamName stream) =>
//         new ReadStreamRequest.Builder()
//             .ForStream(stream);
// }

[PublicAPI]
public record ReadStreamRequest : SelfValidating<ReadStreamRequest, ReadStreamRequest.Validator> {
    public static Builder New() => new();

    /// <summary>
    /// Name of the stream to read from.
    /// </summary>
    public StreamName Stream { get; init; } = StreamName.Unset;

    /// <summary>
    /// Starting revision within the specified stream to begin reading from.
    /// </summary>
    public Revision Start { get; init; } = Revision.Min;

    /// <summary>Maximum number of records to read in this operation.</summary>
    public long Limit { get; init; } = long.MaxValue;

    /// <summary>Record filtering criteria to apply during read operations.</summary>
    public IRecordFilter Filter { get; init; } = RecordFilter.None;

    /// <summary>Direction for reading records through the stream.</summary>
    public ReadDirection Direction { get; init; } = ReadDirection.Forwards;

    /// <summary>Skip record data deserialization for performance-critical scenarios.</summary>
    public bool SkipDecoding { get; init; }

    /// <summary>
    /// Fluent builder for constructing <see cref="ReadStreamRequest"/> instances.
    /// </summary>
    [PublicAPI]
    public class Builder(ReadStreamRequest request) : FluentBuilder<Builder, ReadStreamRequest>(request) {
        public Builder() : this(new()) { }

        /// <inheritdoc />
        protected override ReadStreamRequest OnBuild(ReadStreamRequest request) {
            request.EnsureValid();
            return request;
        }

        /// <summary>
        /// Set the stream to read from.
        /// </summary>
        public Builder Stream(StreamName stream) =>
            With(request => request with { Stream = stream });

        /// <summary>
        /// Start from the beginning of the stream (forwards).
        /// </summary>
        public Builder FromStart() =>
            With(request => request with { Start = Revision.Min, Direction = ReadDirection.Forwards });

        /// <summary>
        /// Start from the end of the stream (backwards).
        /// </summary>
        public Builder FromEnd() =>
            With(request => request with { Start = Revision.Max, Direction = ReadDirection.Backwards });

        /// <summary>
        /// Start reading from a specific stream revision.
        /// </summary>
        public Builder FromRevision(Revision revision) =>
            With(request => request with { Start = revision });

        /// <summary>
        /// Read forwards (toward newer records).
        /// </summary>
        public Builder Forwards() =>
            With(request => request with { Direction = ReadDirection.Forwards });

        /// <summary>
        /// Read backwards (toward older records).
        /// </summary>
        public Builder Backwards() =>
            With(request => request with { Direction = ReadDirection.Backwards });

        /// <summary>
        /// Limit the number of records returned.
        /// </summary>
        public Builder Limit(long maxRecords) =>
            With(request => request with { Limit = maxRecords });

        /// <summary>
        /// Apply a record filter.
        /// </summary>
        public Builder Filter(IRecordFilter filter) =>
            With(request => request with { Filter = filter });

        /// <summary>
        /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
        /// </summary>
        public Builder Filter(string expression) =>
            With(request => request with { Filter = RecordFilter.ByExpression(expression) });

        /// <summary>
        /// Skip record data deserialization.
        /// </summary>
        public Builder SkipDecoding(bool skip = true) =>
            With(request => request with { SkipDecoding = skip });
    }

    public class Validator : Validator<Validator, ReadStreamRequest> {
        public Validator() {
            RuleFor(options => options.Limit)
                .GreaterThanOrEqualTo(1L)
                .WithMessage("MaxRecords must be at least 1.");

            RuleFor(options => options.Stream)
                .Must(stream => stream != StreamName.Unset, "Stream name must be specified when reading from a specific stream.");

            RuleFor(options => options.Stream)
                .Must(stream => !stream.Value.Equals("$all", StringComparison.OrdinalIgnoreCase),
                    "Stream name cannot be '$all' when reading from a specific stream.");

            RuleFor(options => options.Start)
                .Must(start => start != Revision.Unset,
                    "Start revision cannot be Unset when reading from a specific stream. Specify a valid revision.");

            RuleFor(options => options.Start)
                .Must(start => start >= Revision.Min, "Start revision cannot be less than Min.");

            RuleFor(options => options.Start)
                .Must(start => start <= Revision.Max, "Start revision cannot be greater than Max.");

            When(options => options.Direction == ReadDirection.Forwards, () => {
                RuleFor(options => options.Start)
                    .Must(start => start != Revision.Max, "Start revision cannot be Max when reading forwards.");
            });

            When(options => options.Filter.TargetStream != StreamName.Unset, () => {
                RuleFor(options => options.Filter.TargetStream)
                    .Must((opts, targetStream) => targetStream == opts.Stream,
                        "Filter target stream does not match the specified stream to read from.");
            });
        }
    }
}

