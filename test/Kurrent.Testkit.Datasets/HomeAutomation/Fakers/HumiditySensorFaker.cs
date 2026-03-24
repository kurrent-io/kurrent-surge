using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class HumiditySensorFaker : Faker<HumiditySensor> {
    public static readonly HumiditySensorFaker Instance = new();

    public HumiditySensorFaker() {
        CustomInstantiator(f => new HumiditySensor {
            Id                   = $"urn:iot:humiditysensor:{f.Random.Guid()}",
            FriendlyName         = $"Humidity Sensor {f.Random.Number(1, 99)}",
            IsBatteryPowered     = true,
            BatteryLevel         = f.Random.Number(60, 100),
            MeasurementTolerance = f.Random.Double(1.0, 5.0),
            MeasurementPrecision = f.PickRandom(1, 2)
        });
    }
}
