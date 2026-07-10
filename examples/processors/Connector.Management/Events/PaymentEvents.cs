namespace Connector.Management.Events;

public record PaymentPlanCreated(
    string PlanId,
    string CustomerId,
    double Amount,
    string Currency,
    string Frequency,
    DateTimeOffset CreatedAt
);

public record PaymentCollected(
    string PlanId,
    int InstallmentNumber,
    double Amount,
    string Currency,
    DateTimeOffset CollectedAt
);
