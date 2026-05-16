namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public abstract record Device {
    public required string Id               { get; init; }
    public required string FriendlyName     { get; init; }
    public required bool   IsBatteryPowered { get; init; }
    public required int?   BatteryLevel     { get; init; }
}
