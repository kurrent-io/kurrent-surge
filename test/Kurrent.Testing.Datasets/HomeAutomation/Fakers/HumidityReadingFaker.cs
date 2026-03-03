using Bogus;
using Kurrent.Testing.Sample.HomeAutomation;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HumidityReadingFaker : Faker<HumidityReading> {
    public HumidityReadingFaker(HumiditySensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.Humidity, f => {
                    var change = f.Random.Double(-5.0, 5.0);
                    device.UpdateHumidity(device.HumidityLevel + change);

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return Math.Round(device.HumidityLevel, 1);
                }
            )
            .RuleFor(e => e.ComfortLevel, f => {
                var humidity = device.HumidityLevel;
                return humidity switch {
                    < 30 or > 70    => ComfortLevel.Low,
                    >= 40 and <= 60 => ComfortLevel.Optimal,
                    _               => ComfortLevel.High
                };
            })
            .RuleFor(e => e.Timestamp, timestamp);
    }
}