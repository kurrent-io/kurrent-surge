namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record SmokeDetector : Device {
    public required string SensorType     { get; init; } // "photoelectric" | "ionization"
    public required int    AlarmThreshold { get; init; } // smoke level 0-100
}
