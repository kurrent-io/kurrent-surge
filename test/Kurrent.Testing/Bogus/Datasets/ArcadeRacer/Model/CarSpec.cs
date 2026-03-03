namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Performance attributes for a car. Each stat ranges from 1 (worst) to 10 (best).</summary>
/// <param name="Acceleration">Affects lap time via acceleration factor.</param>
/// <param name="TopSpeed">Affects lap time on straights; reduced in rain.</param>
/// <param name="Handling">Affects lap time in corners; reduced at night.</param>
/// <param name="Endurance">Controls tire degradation rate and pit stop window.</param>
public record struct CarSpec(int Acceleration, int TopSpeed, int Handling, int Endurance) {
    /// <summary>Neutral baseline spec with all stats set to 5.</summary>
    public static readonly CarSpec Default = new(5, 5, 5, 5);
}