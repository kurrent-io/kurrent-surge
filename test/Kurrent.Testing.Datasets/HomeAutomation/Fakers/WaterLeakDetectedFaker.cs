using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class WaterLeakEventFaker(Device device, Guid roomId, long timestamp, WeatherCondition weather = WeatherCondition.Sunny) {
    readonly Faker _faker = new();

    public IHomeAutomationEvent Generate() {
        var water = (WaterSensor)device;
        var leakProbability = weather is WeatherCondition.Rainy or WeatherCondition.Stormy ? 0.002f * 1.5f : 0.002f;
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        if (_faker.Random.Bool(leakProbability)) {
            return new WaterLeakDetected {
                Device        = info,
                MoistureLevel = _faker.Random.Number(water.MoistureThreshold, 100),
                Timestamp     = timestamp
            };
        }

        return new WaterLeakResolved {
            Device    = info,
            Timestamp = timestamp
        };
    }
}
