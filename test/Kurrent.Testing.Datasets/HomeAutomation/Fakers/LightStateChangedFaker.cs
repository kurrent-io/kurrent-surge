using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class LightStateChangedFaker : Faker<LightStateChanged> {
    public LightStateChangedFaker(SmartLight device, long timestamp) {
        var isOn = false;
        var brightness = 0;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(e => e.IsOn, f => {
                isOn = f.Random.Bool(0.7f);
                return isOn;
            })
            .RuleFor(e => e.Brightness, f => {
                brightness = isOn ? f.Random.Number(10, 100) : 0;
                return brightness;
            })
            .RuleFor(e => e.Color, f => isOn ? f.Internet.Color() : "#000000")
            .RuleFor(e => e.PowerConsumption, f => isOn ? Math.Round(brightness * 0.8 + f.Random.Double(0, 20), 2) : 0.0)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
