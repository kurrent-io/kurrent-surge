using Humanizer;
using Kurrent.Testing.Sample.HomeAutomation;

namespace Testing.Datasets.HomeAutomation.Model;

/// <summary>
/// Base class for all IoT devices
/// </summary>
public abstract class Device(Room room, DeviceType deviceType, bool isBatteryPowered, string deviceId) {
    public string     Id               { get; init; } = deviceId;
    public string     FriendlyName     { get; init; } = $"{room.Name} {deviceType.Humanize()}";
    public Room       Room             { get; init; } = room;
    public DeviceType DeviceType       { get; init; } = deviceType;
    public bool       IsBatteryPowered { get; init; } = isBatteryPowered;

    public virtual DeviceInfo CreateDeviceInfo(int? batteryLevel = null) =>
        new() {
            Id               = Id,
            Name             = FriendlyName,
            RoomId           = Room.RoomId,
            DeviceType       = DeviceType,
            IsBatteryPowered = IsBatteryPowered,
            BatteryLevel     = IsBatteryPowered ? batteryLevel : null
        };
}