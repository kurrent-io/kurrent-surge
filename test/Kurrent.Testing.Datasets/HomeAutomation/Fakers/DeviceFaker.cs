using Bogus;
using Humanizer;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DeviceFaker : Faker<Device> {
    public DeviceFaker(Room room, DeviceType? deviceType = null, string? homeId = null) {
        CustomInstantiator(f => {
                var actualDeviceType = deviceType ?? SelectWeightedDeviceType(f, room.Type);
                var deviceId         = GenerateDeviceUrn(homeId ?? f.Random.AlphaNumeric(6).ToLower(), actualDeviceType, f);
                var friendlyName     = $"{room.Name} {actualDeviceType.Humanize()}";
                var isBattery        = IsBatteryPowered(actualDeviceType);

                return actualDeviceType switch {
                    DeviceType.Thermostat     => new Thermostat     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.MotionSensor   => new MotionSensor   { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.SmartLight     => new SmartLight     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.DoorLock       => new DoorLock       { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.SmokeDetector  => new SmokeDetector  { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.HumiditySensor => new HumiditySensor { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.WindowSensor   => new WindowSensor   { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.WaterSensor    => new WaterSensor    { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    _                         => new Thermostat     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery }
                };
            }
        );
    }

    static bool IsBatteryPowered(DeviceType type) =>
        type is DeviceType.Thermostat or DeviceType.SmokeDetector or DeviceType.HumiditySensor
            or DeviceType.WindowSensor or DeviceType.WaterSensor;

    static string GenerateDeviceUrn(string homeId, DeviceType deviceType, Faker faker) {
        var deviceTypeName = deviceType.ToString().ToLower();
        var uniqueId       = faker.Internet.Mac().Replace(":", "").ToLower();
        return $"urn:iot:{homeId}:{deviceTypeName}:{uniqueId}";
    }

    static DeviceType SelectWeightedDeviceType(Faker faker, RoomType roomType) {
        var weightedDevices = GetSuitableDeviceTypes(roomType);

        // Calculate total weight
        var totalWeight = weightedDevices.Sum(wd => wd.Weight);
        var randomValue = faker.Random.Float(0f, totalWeight);

        // Select device based on weight
        var runningWeight = 0f;
        foreach (var weightedDevice in weightedDevices) {
            runningWeight += weightedDevice.Weight;
            if (randomValue <= runningWeight) return weightedDevice.DeviceType;
        }

        // Fallback (should not happen)
        return weightedDevices.First().DeviceType;
    }

    static WeightedDevice[] GetSuitableDeviceTypes(RoomType roomType) =>
        roomType switch {
            RoomType.Kitchen => [
                new(DeviceType.MotionSensor, 0.8f),  // Very common
                new(DeviceType.SmartLight, 0.7f),    // Very common
                new(DeviceType.SmokeDetector, 0.6f), // Important for safety
                new(DeviceType.WaterSensor, 0.4f),   // Leak detection
                new(DeviceType.Thermostat, 0.3f)     // Less common in kitchen
            ],
            RoomType.LivingRoom => [
                new(DeviceType.MotionSensor, 0.9f), // Most common
                new(DeviceType.SmartLight, 0.8f),   // Very common
                new(DeviceType.Thermostat, 0.5f),   // Climate control
                new(DeviceType.WindowSensor, 0.3f)  // Security/automation
            ],
            RoomType.Bedroom => [
                new(DeviceType.SmartLight, 0.7f),   // Common
                new(DeviceType.Thermostat, 0.6f),   // Climate comfort
                new(DeviceType.WindowSensor, 0.4f), // Security/automation
                new(DeviceType.MotionSensor, 0.3f)  // Less intrusive
            ],
            RoomType.Bathroom => [
                new(DeviceType.MotionSensor, 0.8f),   // Automation
                new(DeviceType.SmartLight, 0.7f),     // Common
                new(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new(DeviceType.WaterSensor, 0.5f),    // Leak detection
                new(DeviceType.Thermostat, 0.2f)      // Less common
            ],
            RoomType.Office => [
                new(DeviceType.MotionSensor, 0.8f), // Occupancy
                new(DeviceType.SmartLight, 0.7f),   // Task lighting
                new(DeviceType.Thermostat, 0.5f),   // Comfort
                new(DeviceType.WindowSensor, 0.3f)  // Security
            ],
            RoomType.Garage => [
                new(DeviceType.MotionSensor, 0.9f),  // Security/automation
                new(DeviceType.DoorLock, 0.7f),      // Security
                new(DeviceType.SmokeDetector, 0.4f), // Fire safety
                new(DeviceType.WaterSensor, 0.3f)    // Flood detection
            ],
            RoomType.Entrance => [
                new(DeviceType.DoorLock, 0.9f),     // Security priority
                new(DeviceType.MotionSensor, 0.8f), // Security/automation
                new(DeviceType.SmartLight, 0.4f)    // Welcome lighting
            ],
            RoomType.Hallway => [
                new(DeviceType.MotionSensor, 0.9f), // Automation
                new(DeviceType.SmartLight, 0.8f)    // Path lighting
            ],
            RoomType.DiningRoom => [
                new(DeviceType.SmartLight, 0.8f),   // Ambiance
                new(DeviceType.MotionSensor, 0.6f), // Automation
                new(DeviceType.Thermostat, 0.4f)    // Comfort
            ],
            RoomType.Basement => [
                new(DeviceType.MotionSensor, 0.7f),   // Security/automation
                new(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new(DeviceType.WaterSensor, 0.5f),    // Flood detection
                new(DeviceType.SmokeDetector, 0.4f),  // Fire safety
                new(DeviceType.SmartLight, 0.3f)      // Basic lighting
            ],
            RoomType.Utility => [
                new(DeviceType.WaterSensor, 0.8f),    // Leak detection priority
                new(DeviceType.HumiditySensor, 0.6f), // Environmental monitoring
                new(DeviceType.MotionSensor, 0.5f),   // Basic automation
                new(DeviceType.SmartLight, 0.4f)      // Basic lighting
            ],
            _ => [
                new(DeviceType.MotionSensor, 0.5f), // Generic fallback
                new(DeviceType.SmartLight, 0.4f)    // Generic fallback
            ]
        };

    record WeightedDevice(DeviceType DeviceType, float Weight);
}