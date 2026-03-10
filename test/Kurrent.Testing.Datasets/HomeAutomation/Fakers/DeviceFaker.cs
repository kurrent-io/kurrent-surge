using Bogus;
using Humanizer;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DeviceFaker : Faker<Device> {
    public DeviceFaker(RoomType roomType = RoomType.LivingRoom, DeviceType? deviceType = null, string? homeId = null) {
        CustomInstantiator(f => {
                var type         = deviceType ?? SelectWeightedDeviceType(f, roomType);
                var deviceId     = GenerateDeviceUrn(homeId ?? f.Random.AlphaNumeric(6).ToLower(), type, f);
                var friendlyName = $"{roomType.Humanize()} {type.Humanize()}";
                var isBattery    = IsBatteryPowered(type);
                var battery      = isBattery ? f.Random.Number(60, 100) : (int?)null;

                var device = type switch {
                    DeviceType.Thermostat     => (Device)ThermostatFaker.Instance.Generate(),
                    DeviceType.MotionSensor   => MotionSensorFaker.Instance.Generate(),
                    DeviceType.SmartLight     => SmartLightFaker.Instance.Generate(),
                    DeviceType.DoorLock       => DoorLockFaker.Instance.Generate(),
                    DeviceType.SmokeDetector  => SmokeDetectorFaker.Instance.Generate(),
                    DeviceType.HumiditySensor => HumiditySensorFaker.Instance.Generate(),
                    DeviceType.WindowSensor   => WindowSensorFaker.Instance.Generate(),
                    DeviceType.WaterSensor    => WaterSensorFaker.Instance.Generate(),
                    _                         => (Device)ThermostatFaker.Instance.Generate()
                };

                return device with {
                    Id               = deviceId,
                    FriendlyName     = friendlyName,
                    IsBatteryPowered = isBattery,
                    BatteryLevel     = battery
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

            if (randomValue <= runningWeight)
                return weightedDevice.DeviceType;
        }

        // Fallback (should not happen)
        return weightedDevices.First().DeviceType;
    }

    static WeightedDevice[] GetSuitableDeviceTypes(RoomType roomType) =>
        roomType switch {
            RoomType.Kitchen => [
                new WeightedDevice(DeviceType.MotionSensor, 0.8f),  // Very common
                new WeightedDevice(DeviceType.SmartLight, 0.7f),    // Very common
                new WeightedDevice(DeviceType.SmokeDetector, 0.6f), // Important for safety
                new WeightedDevice(DeviceType.WaterSensor, 0.4f),   // Leak detection
                new WeightedDevice(DeviceType.Thermostat, 0.3f)     // Less common in kitchen
            ],
            RoomType.LivingRoom => [
                new WeightedDevice(DeviceType.MotionSensor, 0.9f), // Most common
                new WeightedDevice(DeviceType.SmartLight, 0.8f),   // Very common
                new WeightedDevice(DeviceType.Thermostat, 0.5f),   // Climate control
                new WeightedDevice(DeviceType.WindowSensor, 0.3f)  // Security/automation
            ],
            RoomType.Bedroom => [
                new WeightedDevice(DeviceType.SmartLight, 0.7f),   // Common
                new WeightedDevice(DeviceType.Thermostat, 0.6f),   // Climate comfort
                new WeightedDevice(DeviceType.WindowSensor, 0.4f), // Security/automation
                new WeightedDevice(DeviceType.MotionSensor, 0.3f)  // Less intrusive
            ],
            RoomType.Bathroom => [
                new WeightedDevice(DeviceType.MotionSensor, 0.8f),   // Automation
                new WeightedDevice(DeviceType.SmartLight, 0.7f),     // Common
                new WeightedDevice(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new WeightedDevice(DeviceType.WaterSensor, 0.5f),    // Leak detection
                new WeightedDevice(DeviceType.Thermostat, 0.2f)      // Less common
            ],
            RoomType.Office => [
                new WeightedDevice(DeviceType.MotionSensor, 0.8f), // Occupancy
                new WeightedDevice(DeviceType.SmartLight, 0.7f),   // Task lighting
                new WeightedDevice(DeviceType.Thermostat, 0.5f),   // Comfort
                new WeightedDevice(DeviceType.WindowSensor, 0.3f)  // Security
            ],
            RoomType.Garage => [
                new WeightedDevice(DeviceType.MotionSensor, 0.9f),  // Security/automation
                new WeightedDevice(DeviceType.DoorLock, 0.7f),      // Security
                new WeightedDevice(DeviceType.SmokeDetector, 0.4f), // Fire safety
                new WeightedDevice(DeviceType.WaterSensor, 0.3f)    // Flood detection
            ],
            RoomType.Entrance => [
                new WeightedDevice(DeviceType.DoorLock, 0.9f),     // Security priority
                new WeightedDevice(DeviceType.MotionSensor, 0.8f), // Security/automation
                new WeightedDevice(DeviceType.SmartLight, 0.4f)    // Welcome lighting
            ],
            RoomType.Hallway => [
                new WeightedDevice(DeviceType.MotionSensor, 0.9f), // Automation
                new WeightedDevice(DeviceType.SmartLight, 0.8f)    // Path lighting
            ],
            RoomType.DiningRoom => [
                new WeightedDevice(DeviceType.SmartLight, 0.8f),   // Ambiance
                new WeightedDevice(DeviceType.MotionSensor, 0.6f), // Automation
                new WeightedDevice(DeviceType.Thermostat, 0.4f)    // Comfort
            ],
            RoomType.Basement => [
                new WeightedDevice(DeviceType.MotionSensor, 0.7f),   // Security/automation
                new WeightedDevice(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new WeightedDevice(DeviceType.WaterSensor, 0.5f),    // Flood detection
                new WeightedDevice(DeviceType.SmokeDetector, 0.4f),  // Fire safety
                new WeightedDevice(DeviceType.SmartLight, 0.3f)      // Basic lighting
            ],
            RoomType.Utility => [
                new WeightedDevice(DeviceType.WaterSensor, 0.8f),    // Leak detection priority
                new WeightedDevice(DeviceType.HumiditySensor, 0.6f), // Environmental monitoring
                new WeightedDevice(DeviceType.MotionSensor, 0.5f),   // Basic automation
                new WeightedDevice(DeviceType.SmartLight, 0.4f)      // Basic lighting
            ],
            _ => [
                new WeightedDevice(DeviceType.MotionSensor, 0.5f), // Generic fallback
                new WeightedDevice(DeviceType.SmartLight, 0.4f)    // Generic fallback
            ]
        };

    record WeightedDevice(DeviceType DeviceType, float Weight);
}
