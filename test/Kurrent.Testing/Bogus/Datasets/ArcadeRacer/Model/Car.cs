namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Mutable entity representing a car in the race. Mutated only by the simulation engine.</summary>
public class Car {
    /// <summary>Unique identifier for the car.</summary>
    public Guid      Id     { get; set; } = Guid.Empty;
    /// <summary>Generated display name (e.g. "Thunder Bolt MK-II").</summary>
    public string    Name   { get; set; } = "";
    /// <summary>Grid number (1-based).</summary>
    public int       Number { get; set; }
    /// <summary>Pilot name in abbreviated form (e.g. "J. Smith").</summary>
    public string    Pilot  { get; set; } = "";
    /// <summary>Performance attributes.</summary>
    public CarSpec   Spec   { get; set; } = CarSpec.Default;
    /// <summary>Current racing status.</summary>
    public CarStatus Status { get; set; } = CarStatus.Racing;

    /// <summary>Projects this car into a lightweight <see cref="CarInfo"/> value.</summary>
    /// <returns>An immutable snapshot of the car's identity and spec.</returns>
    public CarInfo ToInfo() => new(Id, Name, Number, Pilot, Spec);
}