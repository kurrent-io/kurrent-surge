using Humanizer;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public record SeasonProfile {
    public static readonly SeasonProfile Spring = new() {
        Name                  = nameof(Spring),
        MinOutdoorTemp        = 10.0,
        MaxOutdoorTemp        = 20.0,
        BaseHumidity          = 45.0,
        DaylightStartHour     = 7,
        DaylightEndHour       = 19,
        WindowOpenProbability = 0.1,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.30,
            [WeatherCondition.Cloudy] = 0.30,
            [WeatherCondition.Rainy]  = 0.25,
            [WeatherCondition.Windy]  = 0.10,
            [WeatherCondition.Stormy] = 0.05
        }
    };

    public static readonly SeasonProfile Summer = new() {
        Name                  = nameof(Summer),
        MinOutdoorTemp        = 20.0,
        MaxOutdoorTemp        = 35.0,
        BaseHumidity          = 55.0,
        DaylightStartHour     = 6,
        DaylightEndHour       = 21,
        WindowOpenProbability = 0.3,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.50,
            [WeatherCondition.Cloudy] = 0.25,
            [WeatherCondition.Rainy]  = 0.15,
            [WeatherCondition.Windy]  = 0.05,
            [WeatherCondition.Stormy] = 0.05
        }
    };

    public static readonly SeasonProfile HotSummer = new() {
        Name                  = nameof(HotSummer),
        MinOutdoorTemp        = 25.0,
        MaxOutdoorTemp        = 40.0,
        BaseHumidity          = 60.0,
        DaylightStartHour     = 6,
        DaylightEndHour       = 21,
        WindowOpenProbability = 0.4,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.55,
            [WeatherCondition.Cloudy] = 0.20,
            [WeatherCondition.Rainy]  = 0.10,
            [WeatherCondition.Windy]  = 0.05,
            [WeatherCondition.Stormy] = 0.10
        }
    };

    public static readonly SeasonProfile Fall = new() {
        Name                  = nameof(Fall),
        MinOutdoorTemp        = 5.0,
        MaxOutdoorTemp        = 15.0,
        BaseHumidity          = 40.0,
        DaylightStartHour     = 8,
        DaylightEndHour       = 17,
        WindowOpenProbability = 0.05,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.20,
            [WeatherCondition.Cloudy] = 0.35,
            [WeatherCondition.Rainy]  = 0.25,
            [WeatherCondition.Windy]  = 0.10,
            [WeatherCondition.Stormy] = 0.10
        }
    };

    public static readonly SeasonProfile Winter = new() {
        Name                  = nameof(Winter),
        MinOutdoorTemp        = -5.0,
        MaxOutdoorTemp        = 10.0,
        BaseHumidity          = 35.0,
        DaylightStartHour     = 8,
        DaylightEndHour       = 16,
        WindowOpenProbability = 0.02,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.15,
            [WeatherCondition.Cloudy] = 0.30,
            [WeatherCondition.Rainy]  = 0.25,
            [WeatherCondition.Windy]  = 0.15,
            [WeatherCondition.Stormy] = 0.15
        }
    };

    public static readonly SeasonProfile ColdWinter = new() {
        Name                  = nameof(ColdWinter),
        MinOutdoorTemp        = -5.0,
        MaxOutdoorTemp        = 10.0,
        BaseHumidity          = 35.0,
        DaylightStartHour     = 8,
        DaylightEndHour       = 16,
        WindowOpenProbability = 0.02,
        WeatherWeights = new Dictionary<WeatherCondition, double> {
            [WeatherCondition.Sunny]  = 0.10,
            [WeatherCondition.Cloudy] = 0.30,
            [WeatherCondition.Rainy]  = 0.20,
            [WeatherCondition.Windy]  = 0.20,
            [WeatherCondition.Stormy] = 0.20
        }
    };

    public required string Name                  { get; init; }
    public required double MinOutdoorTemp        { get; init; }
    public required double MaxOutdoorTemp        { get; init; }
    public required double BaseHumidity          { get; init; }
    public required int    DaylightStartHour     { get; init; }
    public required int    DaylightEndHour       { get; init; }
    public required double WindowOpenProbability { get; init; }

    public required IReadOnlyDictionary<WeatherCondition, double> WeatherWeights { get; init; }

    public override string ToString() => Name.Humanize();
}

public enum WeatherCondition {
    Sunny,
    Cloudy,
    Rainy,
    Windy,
    Stormy
}
