namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record Thermostat : Device {
    public required double          MinOperatingTemp     { get; init; }
    public required double          MaxOperatingTemp     { get; init; }
    public required int             MeasurementPrecision { get; init; } // decimal places
    public required TemperatureUnit PreferredUnit        { get; init; }
}
