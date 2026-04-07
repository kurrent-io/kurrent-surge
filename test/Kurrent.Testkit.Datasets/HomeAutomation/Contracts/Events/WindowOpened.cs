namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record WindowOpened : IHomeAutomationEvent {
    public string EventId { get; init; } = PunyId.New();

    public required DeviceInfo Device         { get; init; }
    public required int        OpenPercentage { get; init; }
    public required long       Timestamp      { get; init; }
}
