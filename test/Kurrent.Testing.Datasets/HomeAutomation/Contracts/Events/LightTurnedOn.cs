namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record LightTurnedOn : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device           { get; init; }
    public required int        Brightness       { get; init; }
    public required string     Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}