using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class MotionDetectedFaker : Faker<MotionDetected> {
    public MotionDetectedFaker(MotionSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(e => e.Confidence, f => Math.Round(f.Random.Double(40.0, 100.0), 1))
            .RuleFor(e => e.ZoneId, f => f.Random.Number(1, 5))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
