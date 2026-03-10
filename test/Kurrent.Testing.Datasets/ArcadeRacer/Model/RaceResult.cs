namespace Testing.Datasets.ArcadeRacer.Model;

/// <summary>Final standings entry for a single car in a completed race.</summary>
/// <param name="CarId">Unique car identifier.</param>
/// <param name="CarName">Generated car name.</param>
/// <param name="Pilot">Pilot name.</param>
/// <param name="Position">Finishing position (1-based). DNF cars are ranked after finishers.</param>
/// <param name="Dnf">True if the car did not finish.</param>
/// <param name="CumulativeTime">Total race time in seconds. Zero for DNF cars.</param>
/// <param name="GapToLeader">Time gap to the race leader in seconds.</param>
/// <param name="LapsCompleted">Number of laps completed before finishing or retiring.</param>
public record struct RaceResult(
    Guid   CarId,
    string CarName,
    string Pilot,
    int    Position,
    bool   Dnf,
    double CumulativeTime,
    double GapToLeader,
    int    LapsCompleted
);
