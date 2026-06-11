namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record WindowClosed : IHomeAutomationEvent {
    public string EventId { get; init; } = TestId.New();

    public required DeviceInfo Device    { get; init; }
    public required long       Timestamp { get; init; }
}
