using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class MotionDetectedFaker : Faker<MotionDetected> {
    public MotionDetectedFaker(Device device, string roomId, long timestamp) {
        var motion = (MotionSensor)device;
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        RuleFor(e => e.EventId, f => TestId.New())
            .RuleFor(e => e.Device, _ => info)
            .RuleFor(e => e.Confidence, f => Math.Round(f.Random.Double(40.0, 100.0), 1))
            .RuleFor(e => e.ZoneId, f => f.Random.Number(1, motion.ZoneCount))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
