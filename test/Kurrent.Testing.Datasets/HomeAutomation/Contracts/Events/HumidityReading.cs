using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record HumidityReading : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}