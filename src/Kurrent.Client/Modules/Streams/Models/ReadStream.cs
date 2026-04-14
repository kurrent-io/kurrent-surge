using FluentValidation;
using Kurrent.Validation;

namespace Kurrent.Client.Streams;

/// <summary>
/// Static entry point for building named stream read requests.
/// </summary>
[PublicAPI]
public static class ReadStream {
    /// <summary>
    /// Start building read request for the specified stream.
    /// </summary>
    public static ReadStreamRequestBuilder Name(StreamName stream) =>
        new ReadStreamRequestBuilder()
            .ForStream(stream);
}

/// <summary>
/// Fluent builder for constructing <see cref="ReadStreamRequest"/> instances.
/// </summary>
[PublicAPI]
public class ReadStreamRequestBuilder : FluentBuilder<ReadStreamRequestBuilder, ReadStreamRequest> {
    public ReadStreamRequestBuilder() : base(new ReadStreamRequest()) { }

    /// <inheritdoc />
    protected override ReadStreamRequest OnBuild(ReadStreamRequest request) {
        request.EnsureValid();
        return request;
    }

    /// <summary>
    /// Set the stream to read from.
    /// </summary>
    public ReadStreamRequestBuilder ForStream(StreamName stream) =>
        With(request => request with { Stream = stream });

    /// <summary>
    /// Set the stream to read from.
    /// </summary>
    public ReadStreamRequestBuilder ForStream(string stream) =>
        With(request => request with { Stream = StreamName.From(stream) });

    /// <summary>
    /// Start from the beginning of the stream (forwards).
    /// </summary>
    public ReadStreamRequestBuilder FromStart() =>
        With(request => request with { Start = Revision.Min, Direction = ReadDirection.Forwards });

    /// <summary>
    /// Start from the end of the stream (backwards).
    /// </summary>
    public ReadStreamRequestBuilder FromEnd() =>
        With(request => request with { Start = Revision.Max, Direction = ReadDirection.Backwards });

    /// <summary>
    /// Start reading from a specific stream revision.
    /// </summary>
    public ReadStreamRequestBuilder FromRevision(Revision revision) =>
        With(request => request with { Start = revision });

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public ReadStreamRequestBuilder Forward() =>
        With(request => request with { Direction = ReadDirection.Forwards });

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public ReadStreamRequestBuilder Backwards() =>
        With(request => request with { Direction = ReadDirection.Backwards });

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public ReadStreamRequestBuilder WithMaxRecords(long maxRecords) =>
        With(request => request with { Limit = maxRecords });

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public ReadStreamRequestBuilder WithFilter(IRecordFilter filter) =>
        With(request => request with { Filter = filter });

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public ReadStreamRequestBuilder WithFilterExpression(string expression) =>
        With(request => request with { Filter = RecordFilter.ByExpression(expression) });

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public ReadStreamRequestBuilder SkipDecoding() =>
        With(request => request with { SkipDecoding = true });
}

[PublicAPI]
public record ReadStreamRequest : SelfValidating<ReadStreamRequest, ReadStreamRequestValidator> {
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
}

public class ReadStreamRequestValidator : FluentValidator<ReadStreamRequestValidator, ReadStreamRequest> {
    public ReadStreamRequestValidator() {
        RuleFor(options => options.Limit)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MaxRecords must be at least 1.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Stream)
            .Must(stream => stream != StreamName.Unset)
            .WithMessage("Stream name must be specified when reading from a specific stream.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Stream)
            .Must(stream => !stream.Value.Equals("$all", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Stream name cannot be '$all' when reading from a specific stream.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Start)
            .Must(start => start != Revision.Unset)
            .WithMessage("Start revision cannot be Unset when reading from a specific stream. Specify a valid revision.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Start)
            .Must(start => start >= Revision.Min)
            .WithMessage("Start revision cannot be less than Min.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Start)
            .Must(start => start <= Revision.Max)
            .WithMessage("Start revision cannot be greater than Max.")
            .WithSeverity(Severity.Error);

        When(options => options.Direction == ReadDirection.Forwards, () => {
            RuleFor(options => options.Start)
                .Must(start => start != Revision.Max)
                .WithMessage("Start revision cannot be Max when reading forwards.")
                .WithSeverity(Severity.Error);
        });

        When(options => options.Filter.TargetStream != StreamName.Unset, () => {
            RuleFor(options => options)
                .Must(opts => opts.Filter.TargetStream == opts.Stream)
                .WithMessage("Filter target stream does not match the specified stream to read from.")
                .WithSeverity(Severity.Error);
        });
    }
}

