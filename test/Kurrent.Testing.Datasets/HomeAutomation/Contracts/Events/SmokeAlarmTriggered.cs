namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record SmokeAlarmTriggered : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device     { get; init; }
    public required int        SmokeLevel { get; init; }
    public required long       Timestamp  { get; init; }
}