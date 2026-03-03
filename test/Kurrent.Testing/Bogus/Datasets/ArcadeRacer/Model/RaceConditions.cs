namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Environmental conditions that affect the <see cref="DifficultyProfile"/>.</summary>
/// <param name="TimeOfDay">Time of day (day or night).</param>
/// <param name="Weather">Weather condition (sunny or rainy).</param>
public record struct RaceConditions(TimeOfDay TimeOfDay, Weather Weather) {
    /// <summary>Sentinel value indicating conditions have not been set.</summary>
    public static readonly RaceConditions Unspecified = new(TimeOfDay.Unspecified, Weather.Unspecified);
    /// <summary>Standard conditions: daytime, sunny.</summary>
    public static readonly RaceConditions Default     = new(TimeOfDay.Day, Weather.Sunny);
}