namespace Testing.Datasets.HomeAutomation.Simulation;

public record SeasonProfile {
    public required string Name                 { get; init; }
    public required double MinOutdoorTemp       { get; init; }
    public required double MaxOutdoorTemp       { get; init; }
    public required double BaseHumidity         { get; init; }
    public required int    DaylightStartHour    { get; init; }
    public required int    DaylightEndHour      { get; init; }
    public required double WindowOpenProbability { get; init; }

    public static readonly SeasonProfile Summer = new() {
        Name                 = "Summer",
        MinOutdoorTemp       = 20.0,
        MaxOutdoorTemp       = 35.0,
        BaseHumidity         = 55.0,
        DaylightStartHour    = 6,
        DaylightEndHour      = 21,
        WindowOpenProbability = 0.3
    };

    public static readonly SeasonProfile Winter = new() {
        Name                 = "Winter",
        MinOutdoorTemp       = -5.0,
        MaxOutdoorTemp       = 10.0,
        BaseHumidity         = 35.0,
        DaylightStartHour    = 8,
        DaylightEndHour      = 16,
        WindowOpenProbability = 0.02
    };
}
