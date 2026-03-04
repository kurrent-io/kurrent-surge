namespace Testing.Datasets.HomeAutomation.Model;

public record SmartHome {
    public required string       Id        { get; init; }
    public required string       Name      { get; init; }
    public required List<Room>   Rooms     { get; init; }
    public required List<Device> Devices   { get; init; }
    public required DateTime     CreatedAt { get; init; }
}
