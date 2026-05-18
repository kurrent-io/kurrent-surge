namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record HumiditySensor : Device {
    public required double MeasurementTolerance  { get; init; } // ± percentage points
    public required int    MeasurementPrecision  { get; init; } // decimal places
}
