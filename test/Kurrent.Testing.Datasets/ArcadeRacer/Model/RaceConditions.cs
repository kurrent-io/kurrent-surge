namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>Environmental conditions that affect the <see cref="DifficultyProfile"/>.</summary>
public class RaceConditions {
    /// <summary>Sentinel value indicating conditions have not been set.</summary>
    public static readonly RaceConditions Unspecified = new() {
        TimeOfDay = TimeOfDay.Unspecified, Weather = Weather.Unspecified
    };

    /// <summary>Standard conditions: daytime, sunny.</summary>
    public static readonly RaceConditions Default = new() {
        TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny
    };

    /// <summary>Time of day (day or night).</summary>
    public TimeOfDay TimeOfDay { get; set; }

    /// <summary>Weather condition (sunny or rainy).</summary>
    public Weather Weather { get; set; }
}
