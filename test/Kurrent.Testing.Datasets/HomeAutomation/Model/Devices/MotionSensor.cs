namespace Testing.Datasets.HomeAutomation.Model;

public sealed record MotionSensor : Device {
    public required int    ZoneCount            { get; init; }
    public required double DetectionRangeMeters { get; init; }
}
