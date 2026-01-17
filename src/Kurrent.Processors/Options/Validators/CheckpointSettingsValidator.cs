using FluentValidation;

namespace Kurrent.Client.Streams;

public class CheckpointSettingsValidator : AbstractValidator<CheckpointSettings> {
    public static readonly CheckpointSettingsValidator Instance = new();

    public CheckpointSettingsValidator() {
        // Interval validation (1-3600000ms, up to 1 hour)
        RuleFor(settings => settings.Interval)
            .InclusiveBetween(1, 3600000)
            .WithMessage("Checkpoint Interval must be between 1 and 3600000 milliseconds.")
            .WithSeverity(Severity.Error);

        // Threshold validation (1-1000000 records)
        RuleFor(settings => settings.Threshold)
            .InclusiveBetween(1, 1000000)
            .WithMessage("Checkpoint Threshold must be between 1 and 1000000.")
            .WithSeverity(Severity.Error);

        // Timeout validation (1-300000ms, up to 5 minutes)
        RuleFor(settings => settings.Timeout)
            .InclusiveBetween(1, 300000)
            .WithMessage("Checkpoint Timeout must be between 1 and 300000 milliseconds.")
            .WithSeverity(Severity.Error);

        // RetentionCount validation (1-100)
        RuleFor(settings => settings.RetentionCount)
            .InclusiveBetween(1, 100)
            .WithMessage("Checkpoint RetentionCount must be between 1 and 100.")
            .WithSeverity(Severity.Error);

        // FailurePolicy validation - must be a valid enum value
        RuleFor(settings => settings.FailurePolicy)
            .IsInEnum()
            .WithMessage("FailurePolicy must be a valid CheckpointFailurePolicy value.")
            .WithSeverity(Severity.Error);
    }
}
