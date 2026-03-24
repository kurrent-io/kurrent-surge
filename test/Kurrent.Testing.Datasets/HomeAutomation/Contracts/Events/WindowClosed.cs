namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record WindowClosed : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device    { get; init; }
    public required long       Timestamp { get; init; }
}