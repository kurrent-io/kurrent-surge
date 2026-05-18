namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record MotionDetected : IHomeAutomationEvent {
    public string EventId { get; init; } = PunyId.New();

    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}
