using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class TemperatureReadingFaker : Faker<TemperatureReading> {
    public TemperatureReadingFaker(Thermostat device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.Temperature, f => Math.Round(f.Random.Double(15.0, 35.0), 1))
            .RuleFor(e => e.Unit, TemperatureUnit.Celsius)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
