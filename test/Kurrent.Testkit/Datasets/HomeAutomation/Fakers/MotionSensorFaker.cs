using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class MotionSensorFaker : Faker<MotionSensor> {
    public static readonly MotionSensorFaker Instance = new();

    public MotionSensorFaker() {
        CustomInstantiator(f => new MotionSensor {
            Id                   = $"urn:iot:motionsensor:{TestId.New()}",
            FriendlyName         = $"Motion Sensor {f.Random.Number(1, 99)}",
            IsBatteryPowered     = false,
            BatteryLevel         = null,
            ZoneCount            = f.Random.Number(2, 6),
            DetectionRangeMeters = f.Random.Double(3.0, 12.0)
        });
    }
}
