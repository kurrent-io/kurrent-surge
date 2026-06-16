namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record LightTurnedOn : IHomeAutomationEvent {
    public string EventId { get; init; } = TestId.New();

    public required DeviceInfo Device           { get; init; }
    public required int        Brightness       { get; init; }
    public required string     Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}
