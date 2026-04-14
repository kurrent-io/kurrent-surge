using FluentValidation;

namespace Kurrent.Processors.Options.Validators;

public class ProcessorSettingsValidator : FluentValidator<ProcessorSettingsValidator, ProcessorSettings> {
    public ProcessorSettingsValidator() {
        // ProcessorId validation - required when checkpointing
        RuleFor(options => options.ProcessorId)
            .NotEmpty()
            .WithMessage("ProcessorId is required for processor identification and checkpointing.")
            .WithSeverity(Severity.Error);

        // MaxParallelism validation (1-32768)
        RuleFor(options => options.MaxParallelism)
            .InclusiveBetween(1, 32768)
            .WithMessage("MaxParallelism must be between 1 and 32768.")
            .WithSeverity(Severity.Error);

        // PartitionKeySelector recommended when MaxParallelism > 1
        // This is a warning to maintain backward compatibility - parallelism works but may not be optimal
        When(options => options.MaxParallelism > 1, () => {
            RuleFor(options => options.Subscription.PartitionKeySelector)
                .NotEmpty()
                .WithMessage("PartitionKeySelector is recommended when MaxParallelism is greater than 1 for optimal partitioning.")
                .WithSeverity(Severity.Warning);
        });

        // Nested validators
        RuleFor(options => options.Batching)
            .SetValidator(BatchingSettingsValidator.Instance);

        RuleFor(options => options.Checkpoint)
            .SetValidator(CheckpointSettingsValidator.Instance);

        RuleFor(options => options.Subscription)
            .SetValidator(SubscriptionSettingsValidator.Instance);

        RuleFor(options => options.Worker)
            .SetValidator(WorkerSettingsValidator.Instance);
    }
}
