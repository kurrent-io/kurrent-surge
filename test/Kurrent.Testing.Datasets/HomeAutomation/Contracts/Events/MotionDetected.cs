namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record MotionDetected : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}