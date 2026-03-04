using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HumidityReadingFaker : Faker<HumidityReading> {
    public HumidityReadingFaker(HumiditySensor device, long timestamp) {
        var humidity = 0.0;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.Humidity, f => {
                humidity = Math.Round(f.Random.Double(20.0, 80.0), 1);
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
