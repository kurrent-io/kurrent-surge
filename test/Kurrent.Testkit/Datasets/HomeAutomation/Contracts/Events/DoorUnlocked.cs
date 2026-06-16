namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record DoorUnlocked : IHomeAutomationEvent {
    public string EventId { get; init; } = TestId.New();

    public required DeviceInfo Device    { get; init; }
    public required string     UserId    { get; init; }
    public required long       Timestamp { get; init; }
}
