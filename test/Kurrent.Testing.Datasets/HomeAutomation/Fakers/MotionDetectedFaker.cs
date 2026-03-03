using Bogus;
using Kurrent.Testing.Sample.HomeAutomation;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class MotionDetectedFaker : Faker<MotionDetected> {
    public MotionDetectedFaker(MotionSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.Confidence, f => {
                    var timeSinceLastMotion    = timestamp - device.LastMotionTime;
                    var minutesSinceLastMotion = timeSinceLastMotion / 60000.0;

                    device.RecordMotion(timestamp);

                    return Math.Round(
                        minutesSinceLastMotion switch {
                            < 5  => f.Random.Double(40, 70),
                            > 60 => f.Random.Double(80, 100),
                            _    => f.Random.Double(60, 90)
                        }, 1
                    );
                }
            )
            .RuleFor(e => e.ZoneId, f => f.Random.Number(1, 5))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}