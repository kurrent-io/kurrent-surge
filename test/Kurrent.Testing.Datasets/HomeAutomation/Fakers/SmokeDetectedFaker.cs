using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class SmokeDetectedFaker : Faker<SmokeDetected> {
    public SmokeDetectedFaker(SmokeDetector device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.HasSmoke, f => {
                    if (f.Random.Bool(0.05f)) device.DepleteBattery();
                    return f.Random.Bool(0.005f);
                }
            )
            .RuleFor(e => e.SmokeLevel, f => f.Random.Number(0, 20))
            .RuleFor(e => e.AlarmStatus, f => f.Random.Bool(0.005f) ? AlarmStatus.Active : AlarmStatus.Normal)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
