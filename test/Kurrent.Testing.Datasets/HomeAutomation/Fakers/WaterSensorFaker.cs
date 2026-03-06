using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class WaterSensorFaker : Faker<WaterSensor> {
    public static readonly WaterSensorFaker Instance = new();

    public WaterSensorFaker() {
        CustomInstantiator(f => new WaterSensor {
            Id                    = $"urn:iot:watersensor:{f.Random.Guid()}",
            FriendlyName          = $"Water Sensor {f.Random.Number(1, 99)}",
            IsBatteryPowered      = true,
            BatteryLevel          = f.Random.Number(60, 100),
            MoistureThreshold     = f.Random.Number(30, 70),
            ContinuousMeasurement = f.Random.Bool(0.4f)
        });
    }
}
