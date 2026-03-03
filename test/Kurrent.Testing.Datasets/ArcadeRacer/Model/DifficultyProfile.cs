namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Simulation tuning parameters derived from <see cref="CircuitDifficulty"/> and <see cref="RaceConditions"/>.</summary>
public record DifficultyProfile {
    /// <summary>Sentinel value indicating no profile has been computed.</summary>
    public static readonly DifficultyProfile Unspecified = new();

    /// <summary>Multiplier for handling stat effectiveness. Reduced at night.</summary>
    public double HandlingEffectiveness { get; init; } = 1.0;

    /// <summary>Multiplier for top speed stat effectiveness. Reduced in rain.</summary>
    public double TopSpeedEffectiveness { get; init; } = 1.0;

    /// <summary>Per-lap probability of an incident occurring (0.0 to 1.0).</summary>
    public double IncidentRate { get; init; } = 0.05;

    /// <summary>Probability that an incident results in a DNF (0.0 to 1.0).</summary>
    public double DnfRate { get; init; } = 0.30;

    /// <summary>Per-lap probability of a weather change (0.0 to 1.0).</summary>
    public double WeatherChangeRate { get; init; } = 0.03;

    /// <summary>Scalar applied to all computed lap times. Higher values slow the field.</summary>
    public double LapTimeModifier { get; init; } = 1.0;

    /// <summary>Computes a profile from circuit difficulty and current conditions.</summary>
    /// <param name="difficulty">Circuit difficulty tier.</param>
    /// <param name="conditions">Current weather and time-of-day.</param>
    /// <returns>A tuned <see cref="DifficultyProfile"/>.</returns>
    public static DifficultyProfile For(CircuitDifficulty difficulty, RaceConditions conditions) {
        var nightMul = conditions.TimeOfDay == TimeOfDay.Night ? 1.5 : 1.0;
        var rainMul  = conditions.Weather == Weather.Rainy ? 1.8 : 1.0;

        return new DifficultyProfile {
            HandlingEffectiveness = conditions.TimeOfDay == TimeOfDay.Night ? 0.8 : 1.0,
            TopSpeedEffectiveness = conditions.Weather == Weather.Rainy ? 0.7 : 1.0,
            IncidentRate = difficulty switch {
                CircuitDifficulty.Easy => 0.02,
                CircuitDifficulty.Hard => 0.08,
                _                      => 0.05
            } * nightMul * rainMul,
            DnfRate = 0.30,
            WeatherChangeRate = difficulty switch {
                CircuitDifficulty.Easy => 0.01f,
                CircuitDifficulty.Hard => 0.05f,
                _                      => 0.03f
            },
            LapTimeModifier = difficulty switch {
                CircuitDifficulty.Easy => 0.98,
                CircuitDifficulty.Hard => 1.02,
                _                      => 1.0
            }
        };
    }
}
