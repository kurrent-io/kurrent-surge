using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class WaterLeakDetectedFaker : Faker<WaterLeakDetected> {
    public WaterLeakDetectedFaker(WaterSensor device, long timestamp) {
        var hasWater = false;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.HasWater, f => {
                hasWater = f.Random.Bool(0.002f);
                return hasWater;
            })
            .RuleFor(e => e.MoistureLevel, f => hasWater ? f.Random.Number(1, 100) : 0)
            .RuleFor(e => e.AlertSent, f => hasWater && f.Random.Bool(0.9f))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
