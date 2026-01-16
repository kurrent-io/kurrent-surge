using FluentValidation;

namespace Kurrent.Client.Streams;

public class SubscriptionSettingsValidator : AbstractValidator<SubscriptionSettings> {
    public static readonly SubscriptionSettingsValidator Instance = new();

    public SubscriptionSettingsValidator() {
        // InitialPosition validation - must be a valid enum value
        RuleFor(settings => settings.InitialPosition)
            .IsInEnum()
            .WithMessage("InitialPosition must be a valid SubscriptionInitialPosition value.")
            .WithSeverity(Severity.Error);
    }
}
