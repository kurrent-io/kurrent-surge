using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class WindowStateChangedFaker : Faker<WindowStateChanged> {
    public WindowStateChangedFaker(WindowSensor device, long timestamp) {
        var isOpen = false;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.IsOpen, f => {
                isOpen = f.Random.Bool(0.4f);
                return isOpen;
            })
            .RuleFor(e => e.OpenPercentage, f => isOpen ? f.Random.Number(10, 100) : 0)
            .RuleFor(e => e.WeatherCondition, f => f.Random.Enum<WeatherCondition>())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
