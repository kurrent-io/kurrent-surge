using Testing.Datasets.ArcadeRacer.Contracts;

namespace Testing.Datasets.ArcadeRacer.Model;

/// <summary>
/// Complete record of a finished race — the logbook. Contains the full setup,
/// final standings, and every event that occurred during the race.
/// Built as a projection over the event stream.
/// </summary>
public record CompletedRace {
    /// <summary>Cars that started the race.</summary>
    public required RaceCar[] Cars { get; init; }

    /// <summary>Circuit the race was held on.</summary>
    public required Circuit Circuit { get; init; }

    /// <summary>Weather and time-of-day conditions at the start.</summary>
    public required RaceConditions Conditions { get; init; }

    /// <summary>Final standings ordered by position (finishers first, then DNFs).</summary>
    public required RaceResult[] Results { get; init; }

    /// <summary>Total laps completed in the race.</summary>
    public required int TotalLaps { get; init; }

    /// <summary>The race winner (first position).</summary>
    public RaceResult Winner => Results[0];

    /// <summary>Every event produced during the race, in order.</summary>
    public required IReadOnlyList<IRaceEvent> Events { get; init; }
}
