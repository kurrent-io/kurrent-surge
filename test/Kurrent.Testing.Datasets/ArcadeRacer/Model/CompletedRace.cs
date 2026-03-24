using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>
/// Complete record of a finished race — the logbook. Contains the full setup,
/// final standings, and every event that occurred during the race.
/// Built as a projection over the event stream.
/// </summary>
public class CompletedRace {
    /// <summary>Cars that started the race.</summary>
    public RaceCar[] Cars { get; set; } = [];

    /// <summary>Circuit the race was held on.</summary>
    public Circuit Circuit { get; set; } = new();

    /// <summary>Weather and time-of-day conditions at the start.</summary>
    public RaceConditions Conditions { get; set; } = new();

    /// <summary>Final standings ordered by position (finishers first, then DNFs).</summary>
    public RaceResult[] Results { get; set; } = [];

    /// <summary>Total laps completed in the race.</summary>
    public int TotalLaps { get; set; }

    /// <summary>The race winner (first position).</summary>
    public RaceResult Winner => Results[0];

    /// <summary>Every event produced during the race, in order.</summary>
    public IReadOnlyList<IRaceEvent> Events { get; set; } = [];
}
