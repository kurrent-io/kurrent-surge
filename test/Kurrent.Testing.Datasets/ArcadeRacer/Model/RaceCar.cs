namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>Reference data for a car on the grid.</summary>
public class RaceCar {
    /// <summary>Unique identifier for the car.</summary>
    public Guid Id { get; set; }

    /// <summary>Generated display name (e.g. "Thunder Bolt MK-II").</summary>
    public string Name { get; set; } = "";

    /// <summary>Grid number (1-based).</summary>
    public int Number { get; set; }

    /// <summary>Pilot name in abbreviated form (e.g. "J. Smith").</summary>
    public string Pilot { get; set; } = "";

    /// <summary>Performance attributes.</summary>
    public RaceCarSpec Spec { get; set; } = RaceCarSpec.Default;
}
