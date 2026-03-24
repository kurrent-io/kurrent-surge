using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class WindowSensorFaker : Faker<WindowSensor> {
    public static readonly WindowSensorFaker Instance = new();

    public WindowSensorFaker() {
        CustomInstantiator(f => new WindowSensor {
            Id                  = $"urn:iot:windowsensor:{f.Random.Guid()}",
            FriendlyName        = $"Window Sensor {f.Random.Number(1, 99)}",
            IsBatteryPowered    = true,
            BatteryLevel        = f.Random.Number(60, 100),
            SupportsPartialOpen = f.Random.Bool(0.3f)
        });
    }
}
