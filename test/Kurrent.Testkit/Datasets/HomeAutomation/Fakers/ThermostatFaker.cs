using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class ThermostatFaker : Faker<Thermostat> {
    public static readonly ThermostatFaker Instance = new();

    public ThermostatFaker() {
        CustomInstantiator(f => new Thermostat {
            Id                   = $"urn:iot:thermostat:{TestId.New()}",
            FriendlyName         = $"Thermostat {f.Random.Number(1, 99)}",
            IsBatteryPowered     = true,
            BatteryLevel         = f.Random.Number(60, 100),
            MinOperatingTemp     = f.Random.Double(0, 10),
            MaxOperatingTemp     = f.Random.Double(35, 50),
            MeasurementPrecision = f.PickRandom(1, 2),
            PreferredUnit        = TemperatureUnit.Celsius
        });
    }
}
