using Bogus;
using Kurrent.Testing.Sample.HomeAutomation;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class WindowStateChangedFaker : Faker<WindowStateChanged> {
    public WindowStateChangedFaker(WindowSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.IsOpen, f => {
                    if (f.Random.Bool(0.1f)) device.ToggleWindow();
                    if (f.Random.Bool(0.05f)) device.DepleteBattery();

                    return device.IsOpen;
                }
            )
            .RuleFor(e => e.OpenPercentage, f => device.IsOpen ? f.Random.Number(10, 100) : 0)
            .RuleFor(e => e.WeatherCondition, f => f.Random.Enum<WeatherCondition>())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}