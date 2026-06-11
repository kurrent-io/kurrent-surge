namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record WaterLeakDetected : IHomeAutomationEvent {
    public string EventId { get; init; } = TestId.New();

    public required DeviceInfo Device        { get; init; }
    public required int        MoistureLevel { get; init; }
    public required long       Timestamp     { get; init; }
}
