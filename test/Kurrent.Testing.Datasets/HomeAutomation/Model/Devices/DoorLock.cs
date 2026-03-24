namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public sealed record DoorLock : Device {
    public required bool HasKeypad  { get; init; }
    public required bool HasAutoLock { get; init; }
}
