using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class MotionDetectedFaker : Faker<MotionDetected> {
    public MotionDetectedFaker(Device device, Guid roomId, long timestamp) {
        var motion = (MotionSensor)device;
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, _ => info)
            .RuleFor(e => e.Confidence, f => Math.Round(f.Random.Double(40.0, 100.0), 1))
            .RuleFor(e => e.ZoneId, f => f.Random.Number(1, motion.ZoneCount))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
