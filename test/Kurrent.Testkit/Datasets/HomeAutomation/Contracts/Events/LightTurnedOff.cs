namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record LightTurnedOff : IHomeAutomationEvent {
    public string EventId { get; init; } = PunyId.New();

    public required DeviceInfo Device           { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}
