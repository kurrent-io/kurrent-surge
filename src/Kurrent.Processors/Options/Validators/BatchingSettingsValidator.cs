using FluentValidation;

namespace Kurrent.Client.Streams;

public class BatchingSettingsValidator : AbstractValidator<BatchingSettings> {
    public static readonly BatchingSettingsValidator Instance = new();

    public BatchingSettingsValidator() {
        // MaxSize validation (1-10000)
        RuleFor(settings => settings.MaxSize)
            .InclusiveBetween(1, 10000)
            .WithMessage("Batching MaxSize must be between 1 and 10000.")
            .WithSeverity(Severity.Error);

        // Timeout validation (1-300000ms, up to 5 minutes)
        RuleFor(settings => settings.Timeout)
            .InclusiveBetween(1, 300000)
            .WithMessage("Batching Timeout must be between 1 and 300000 milliseconds.")
            .WithSeverity(Severity.Error);
    }
}
