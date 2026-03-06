namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record LightTurnedOff : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device           { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}