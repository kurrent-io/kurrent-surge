namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>Performance attributes for a car. Each stat ranges from 1 (worst) to 10 (best).</summary>
public class RaceCarSpec {
    /// <summary>Neutral baseline spec with all stats set to 5.</summary>
    public static readonly RaceCarSpec Default = new() {
        Acceleration = 5, TopSpeed = 5, Handling = 5, Endurance = 5
    };

    /// <summary>Affects lap time via acceleration factor.</summary>
    public int Acceleration { get; set; }

    /// <summary>Affects lap time on straights; reduced in rain.</summary>
    public int TopSpeed { get; set; }

    /// <summary>Affects lap time in corners; reduced at night.</summary>
    public int Handling { get; set; }

    /// <summary>Controls tire degradation rate and pit stop window.</summary>
    public int Endurance { get; set; }
}