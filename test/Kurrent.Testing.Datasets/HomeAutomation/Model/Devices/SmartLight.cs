namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record SmartLight : Device {
    public required int    MaxBrightness          { get; init; }
    public required bool   SupportsColor          { get; init; }
    public required double WattsPerBrightnessUnit { get; init; }
}
