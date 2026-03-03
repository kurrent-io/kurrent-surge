using Bogus;
using Kurrent.Testing.Sample.HomeAutomation;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class LightStateChangedFaker : Faker<LightStateChanged> {
    public LightStateChangedFaker(SmartLight device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.IsOn, f => {
                    if (f.Random.Bool(0.15f)) device.ToggleLight();
                    if (device.IsOn && f.Random.Bool(0.1f))
                        device.SetBrightness(device.Brightness + f.Random.Number(-20, 20));

                    return device.IsOn;
                }
            )
            .RuleFor(e => e.Brightness, f => device.Brightness)
            .RuleFor(e => e.Color, f => f.Internet.Color())
            .RuleFor(e => e.PowerConsumption, f => device.IsOn ? Math.Round(device.Brightness * 0.8 + f.Random.Double(0, 20), 2) : 0.0)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}