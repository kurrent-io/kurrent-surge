
namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

public record DeviceInfo {
    public required string Id           { get; init; }
    public required Guid   RoomId       { get; init; }
    public          int?   BatteryLevel { get; init; }
}

public interface IHomeAutomationEvent {
    Guid       EventId   { get; init; }
    DeviceInfo Device    { get; init; }
    long       Timestamp { get; init; }
}
