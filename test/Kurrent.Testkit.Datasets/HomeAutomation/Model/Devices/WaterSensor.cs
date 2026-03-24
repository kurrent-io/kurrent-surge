namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record WaterSensor : Device {
    public required int  MoistureThreshold     { get; init; } // 0-100
    public required bool ContinuousMeasurement { get; init; }
}
