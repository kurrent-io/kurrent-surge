using FluentValidation;

namespace Kurrent.Client.Streams;

public class WorkerSettingsValidator : AbstractValidator<WorkerSettings> {
    public static readonly WorkerSettingsValidator Instance = new();

    public WorkerSettingsValidator() {
        RuleFor(settings => settings.BufferCapacity)
            .InclusiveBetween(1, 10000)
            .WithMessage("BufferCapacity must be between 1 and 10000.")
            .WithSeverity(Severity.Error);
    }
}
