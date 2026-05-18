using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>
/// Complete record of a finished race — the logbook. Contains the full setup,
/// final standings, and every event that occurred during the race.
/// Built as a projection over the event stream.
/// </summary>
public class RaceLogbook {
    /// <summary>
    /// Unique identifier for the race this logbook is associated with.
    /// </summary>
    public string RaceId { get; set; }

    /// <summary>Cars that started the race.</summary>
    public IReadOnlyList<RaceCar> Cars { get; set; } = [];

    /// <summary>Circuit the race was held on.</summary>
    public Circuit Circuit { get; set; } = new();

    /// <summary>Weather and time-of-day conditions at the start.</summary>
    public RaceConditions Conditions { get; set; } = new();

    /// <summary>Final standings ordered by position (finishers first, then DNFs).</summary>
    public IReadOnlyList<RaceResult> Results { get; set; } = [];

    /// <summary>Total laps completed in the race.</summary>
    public int TotalLaps { get; set; }

    /// <summary>The race winner (first position).</summary>
    public RaceResult Winner => Results[0];

    /// <summary>Every event produced during the race, in order.</summary>
    public IReadOnlyList<IRaceEvent> Events { get; set; } = [];

    internal static RaceLogbook From(IReadOnlyList<IRaceEvent> raceEvents) {
        var (laps, results) = raceEvents switch {
            _ when raceEvents.OfType<RaceFinished>().SingleOrDefault() is { } f => (f.TotalLaps, f.Results),
            _ when raceEvents.OfType<RaceInsight>().SingleOrDefault()  is { } i => (i.TotalLaps, i.Results)
        };

        var gridFormed = raceEvents.OfType<GridFormed>().Single();

        return new RaceLogbook {
            RaceId = gridFormed.RaceId,
            Cars       = gridFormed.Cars,
            Circuit    = gridFormed.Circuit,
            Conditions = gridFormed.Conditions,
            Results    = results,
            TotalLaps  = laps,
            Events     = raceEvents
        };
    }
}
