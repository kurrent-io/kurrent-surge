namespace Testing.Datasets.HomeAutomation.Model;

public sealed record WindowSensor : Device {
    public required bool SupportsPartialOpen { get; init; }
}
