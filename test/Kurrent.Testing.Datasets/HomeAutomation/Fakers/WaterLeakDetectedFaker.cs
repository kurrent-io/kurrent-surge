using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class WaterLeakDetectedFaker : Faker<WaterLeakDetected> {
    public WaterLeakDetectedFaker(WaterSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.HasWater, f => {
                    // Water detection is rare but serious
                    var hasWater = f.Random.Bool(0.002f); // 0.2% chance
                    if (hasWater) {
                        var waterLevel = f.Random.Number(1, 100);
                        device.UpdateWaterLevel(waterLevel);
                    } else {
                        device.DetectWater(false);
                    }

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return device.WaterDetected;
                }
            )
            .RuleFor(e => e.MoistureLevel, f => device.WaterLevel)
            .RuleFor(e => e.AlertSent, f => device.WaterDetected && f.Random.Bool(0.9f)) // Alert sent 90% of time when water detected
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
