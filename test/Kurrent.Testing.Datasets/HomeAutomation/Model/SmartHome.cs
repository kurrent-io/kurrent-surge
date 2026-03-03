namespace Testing.Datasets.HomeAutomation.Model;

/// <summary>
/// Represents a home with rooms
/// </summary>
public class SmartHome {
    public string       Id        { get; set; } = "";
    public string       Name      { get; set; } = "";
    public List<Room>   Rooms     { get; set; } = [];
    public List<Device> Devices   { get; set; } = [];
    public DateTime     CreatedAt { get; set; } = DateTime.UtcNow;
}