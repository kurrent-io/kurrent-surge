using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class TemperatureReadingFaker : Faker<TemperatureReading> {
    public TemperatureReadingFaker(Device device, Guid roomId, long timestamp, WeatherCondition weather = WeatherCondition.Sunny) {
        var thermo = (Thermostat)device;
        var (tempOffset, _, _) = WeatherModifiers.For(weather);
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, _ => info)
            .RuleFor(e => e.Temperature, f => Math.Round(f.Random.Double(thermo.MinOperatingTemp + tempOffset, thermo.MaxOperatingTemp + tempOffset), thermo.MeasurementPrecision))
            .RuleFor(e => e.Unit, thermo.PreferredUnit)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
