using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class SmokeDetectedFaker : Faker<SmokeDetected> {
    public SmokeDetectedFaker(SmokeDetector device, long timestamp) {
        var hasSmoke = false;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.HasSmoke, f => {
                hasSmoke = f.Random.Bool(0.005f);
                return hasSmoke;
            })
            .RuleFor(e => e.SmokeLevel, f => hasSmoke ? f.Random.Number(10, 80) : 0)
            .RuleFor(e => e.AlarmStatus, f => hasSmoke ? AlarmStatus.Active : AlarmStatus.Normal)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
