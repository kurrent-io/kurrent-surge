using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class HumidityReadingFaker : Faker<HumidityReading> {
    public HumidityReadingFaker(Device device, string roomId, long timestamp, WeatherCondition weather = WeatherCondition.Sunny) {
        var sensor = (HumiditySensor)device;
        var (_, humidityOffset, _) = WeatherModifiers.For(weather);
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        var humidity = 0.0;
        RuleFor(e => e.EventId, f => PunyId.New())
            .RuleFor(e => e.Device, _ => info)
            .RuleFor(e => e.Humidity, f => {
                humidity = Math.Round(Math.Clamp(f.Random.Double(20.0, 80.0) + humidityOffset, 0, 100), sensor.MeasurementPrecision);
                return humidity;
            })
            .RuleFor(e => e.ComfortLevel, f => humidity switch {
                < 30  => ComfortLevel.Low,
                > 60  => ComfortLevel.High,
                _     => ComfortLevel.Optimal
            })
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
