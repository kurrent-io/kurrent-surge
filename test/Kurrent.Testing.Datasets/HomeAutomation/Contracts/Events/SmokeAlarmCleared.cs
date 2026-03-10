namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record SmokeAlarmCleared : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device    { get; init; }
    public required long       Timestamp { get; init; }
}