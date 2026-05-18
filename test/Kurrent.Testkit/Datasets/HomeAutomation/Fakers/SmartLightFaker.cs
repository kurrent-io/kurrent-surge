using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class SmartLightFaker : Faker<SmartLight> {
    public static readonly SmartLightFaker Instance = new();

    public SmartLightFaker() {
        CustomInstantiator(f => new SmartLight {
            Id                     = $"urn:iot:smartlight:{PunyId.New()}",
            FriendlyName           = $"Smart Light {f.Random.Number(1, 99)}",
            IsBatteryPowered       = false,
            BatteryLevel           = null,
            MaxBrightness          = f.PickRandom(100, 150, 200, 255),
            SupportsColor          = f.Random.Bool(0.6f),
            WattsPerBrightnessUnit = f.Random.Double(0.3, 0.8)
        });
    }
}
