using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class WindowEventFaker(Device device, string roomId, long timestamp, WeatherCondition weather = WeatherCondition.Sunny) {
    readonly Faker _faker = new();

    public IHomeAutomationEvent Generate() {
        var window = (WindowSensor)device;
        var (_, _, windowCloseFactor) = WeatherModifiers.For(weather);
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        if (_faker.Random.Bool((float)(0.4 / windowCloseFactor))) {
            return new WindowOpened {
                Device         = info,
                OpenPercentage = window.SupportsPartialOpen ? _faker.Random.Number(10, 100) : 100,
                Timestamp      = timestamp
            };
        }

        return new WindowClosed {
            Device    = info,
            Timestamp = timestamp
        };
    }
}
