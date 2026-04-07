using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record HumidityReading : IHomeAutomationEvent {
    public string EventId { get; init; } = PunyId.New();

    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}
