using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class LightEventFaker(Device device, string roomId, long timestamp) {
    readonly Faker _faker = new();

    public IHomeAutomationEvent Generate() {
        var light = (SmartLight)device;
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        if (_faker.Random.Bool(0.7f)) {
            var brightness = _faker.Random.Number(10, light.MaxBrightness);
            return new LightTurnedOn {
                Device           = info,
                Brightness       = brightness,
                Color            = light.SupportsColor ? _faker.Internet.Color() : "#FFFFFF",
                PowerConsumption = Math.Round(brightness * light.WattsPerBrightnessUnit + _faker.Random.Double(0, 20), 2),
                Timestamp        = timestamp
            };
        }

        return new LightTurnedOff {
            Device           = info,
            PowerConsumption = 0,
            Timestamp        = timestamp
        };
    }
}
