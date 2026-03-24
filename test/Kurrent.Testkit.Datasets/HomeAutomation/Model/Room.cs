namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public record Room {
    public required Guid         RoomId  { get; init; }
    public required string       Name    { get; init; }
    public required RoomType     Type    { get; init; }
    public required List<Device> Devices { get; init; }

}
