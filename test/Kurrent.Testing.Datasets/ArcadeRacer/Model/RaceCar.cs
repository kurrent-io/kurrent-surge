namespace Testing.Datasets.ArcadeRacer.Model;

/// <summary>Immutable reference data for a car on the grid.</summary>
public record RaceCar {
    /// <summary>Unique identifier for the car.</summary>
    public Guid    Id     { get; init; }
    /// <summary>Generated display name (e.g. "Thunder Bolt MK-II").</summary>
    public string  Name   { get; init; } = "";
    /// <summary>Grid number (1-based).</summary>
    public int     Number { get; init; }
    /// <summary>Pilot name in abbreviated form (e.g. "J. Smith").</summary>
    public string  Pilot  { get; init; } = "";
    /// <summary>Performance attributes.</summary>
    public RaceCarSpec Spec   { get; init; } = RaceCarSpec.Default;
}
