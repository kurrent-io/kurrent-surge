using FluentValidation;
using Kurrent.Client.Streams;

namespace Kurrent.Client;

public class KurrentClientReadOptionsValidator : FluentOptionsValidator<KurrentClientReadOptionsValidator, ReadOptions> {
    public KurrentClientReadOptionsValidator() {
        RuleFor(options => options.BufferCapacity)
            .InclusiveBetween(1, 10000)
            .WithMessage("BufferCapacity must be between 1 and 10000.")
            .WithSeverity(Severity.Error);

        RuleFor(options => options.MaxRecords)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MaxRecords must be at least 1.")
            .WithSeverity(Severity.Error);
    }
}
