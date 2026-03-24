using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class SmokeDetectorFaker : Faker<SmokeDetector> {
    public static readonly SmokeDetectorFaker Instance = new();

    public SmokeDetectorFaker() {
        CustomInstantiator(f => new SmokeDetector {
            Id               = $"urn:iot:smokedetector:{f.Random.Guid()}",
            FriendlyName     = $"Smoke Detector {f.Random.Number(1, 99)}",
            IsBatteryPowered = true,
            BatteryLevel     = f.Random.Number(60, 100),
            SensorType       = f.PickRandom("photoelectric", "ionization"),
            AlarmThreshold   = f.Random.Number(40, 80)
        });
    }
}
