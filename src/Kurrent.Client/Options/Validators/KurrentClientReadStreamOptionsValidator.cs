using FluentValidation;

namespace Kurrent.Client.Streams;

public class KurrentClientReadStreamOptionsValidator : FluentOptionsValidator<KurrentClientReadStreamOptionsValidator, ReadStreamOptions> {
    public KurrentClientReadStreamOptionsValidator() {
        RuleFor(options => options.BufferCapacity)
            .InclusiveBetween(1, 10000)
            .WithMessage("BufferCapacity must be between 1 and 10000.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.MaxRecords)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MaxRecords must be at least 1.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Stream)
            .Must(stream => stream != StreamName.Unset)
            .WithMessage("Stream name must be specified when reading from a specific stream.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.Stream)
            .Must(stream => !stream.IsAllStream)
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
