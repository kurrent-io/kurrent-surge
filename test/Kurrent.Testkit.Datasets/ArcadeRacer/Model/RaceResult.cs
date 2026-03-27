namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>Standings entry for a single car in a race.</summary>
public class RaceResult {
    /// <summary>Unique car identifier.</summary>
    public Guid CarId { get; set; }

    /// <summary>Generated car name.</summary>
    public string CarName { get; set; } = "";

    /// <summary>Pilot name.</summary>
    public string Pilot { get; set; } = "";

    /// <summary>Finishing position (1-based). DNF cars are ranked after finishers.</summary>
    public int Position { get; set; }

    /// <summary>True if the car did not finish.</summary>
    public bool Dnf { get; set; }

    /// <summary>Total race time in seconds. Zero for DNF cars.</summary>
    public double CumulativeTime { get; set; }

    /// <summary>Time gap to the race leader in seconds.</summary>
    public double GapToLeader { get; set; }

    /// <summary>Number of laps completed before finishing or retiring.</summary>
    public int LapsCompleted { get; set; }
}
