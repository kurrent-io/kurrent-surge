using Humanizer;
using Testing.Datasets.HomeAutomation.Contracts.Events;

namespace Testing.Datasets.HomeAutomation.Model;

public abstract record Device {
    public required string     Id               { get; init; }
    public required string     FriendlyName     { get; init; }
    public required Room       Room             { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }

    public DeviceInfo CreateDeviceInfo(int? batteryLevel = null) =>
        new() {
            Id               = Id,
            Name             = FriendlyName,
            RoomId           = Room.RoomId,
            DeviceType       = DeviceType,
            IsBatteryPowered = IsBatteryPowered,
            BatteryLevel     = IsBatteryPowered ? batteryLevel : null
        };
}
