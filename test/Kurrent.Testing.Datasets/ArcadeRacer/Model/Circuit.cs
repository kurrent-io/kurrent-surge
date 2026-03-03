namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>A racing circuit with layout characteristics that influence lap time calculations.</summary>
public class Circuit {
    /// <summary>Sentinel value indicating no circuit has been assigned.</summary>
    public static readonly Circuit Unspecified = new();

    /// <summary>Unique identifier for the circuit.</summary>
    public Guid Id { get; set; } = Guid.Empty;

    /// <summary>Generated circuit name (e.g. "Silvervale").</summary>
    public string Name { get; set; } = "";

    /// <summary>Country where the circuit is located.</summary>
    public string Country { get; set; } = "";

    /// <summary>Number of corners. Higher values favor cars with better handling.</summary>
    public int Corners { get; set; }

    /// <summary>Number of straights. Higher values favor cars with better top speed.</summary>
    public int Straights { get; set; }

    /// <summary>Track length in kilometers. Scales the base lap time.</summary>
    public double LengthKm { get; set; }

    /// <summary>Difficulty tier affecting incident rates and lap time modifiers.</summary>
    public CircuitDifficulty Difficulty { get; set; } = CircuitDifficulty.Medium;
}
