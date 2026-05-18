using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Model;

/// <summary>Mutable state of a race, mutated in place by the <see cref="ArcadeRace"/> aggregate.</summary>
public class ArcadeRaceState {
    /// <summary>All cars entered in the race, keyed by <see cref="RaceCar.Id"/>.</summary>
    public Dictionary<string, RaceCar> Cars { get; private set; } = [];

    /// <summary>IDs of cars that retired (Did Not Finish).</summary>
    public HashSet<string> Retired { get; private set; } = [];

    /// <summary>Total elapsed race time per car, keyed by <see cref="RaceCar.Id"/>. Only active cars.</summary>
    public Dictionary<string, double> CumulativeTimes { get; private set; } = [];

    /// <summary>Number of laps completed since the last pit stop, keyed by <see cref="RaceCar.Id"/>. Only active cars.</summary>
    public Dictionary<string, int> LapsSincePit { get; private set; } = [];

    /// <summary>The circuit being raced on.</summary>
    public Circuit Circuit { get; private set; } = Circuit.Unspecified;

    /// <summary>Current weather and time-of-day conditions.</summary>
    public RaceConditions Conditions { get; private set; } = RaceConditions.Unspecified;

    /// <summary>Active difficulty profile derived from circuit difficulty and conditions.</summary>
    public DifficultyProfile Profile { get; private set; } = DifficultyProfile.Unspecified;

    /// <summary>Last completed lap number. Zero means no laps completed yet.</summary>
    public int Lap { get; private set; }

    /// <summary>Total number of laps in the race. Zero means unlimited.</summary>
    public int TotalLaps { get; private set; }

    /// <summary>Unix timestamp in milliseconds for this state snapshot.</summary>
    public long Timestamp { get; private set; }

    /// <summary>True if at least one car is still racing.</summary>
    public bool IsActive => CumulativeTimes.Count > 0;

    /// <summary>Active car IDs in position order (leader first), derived from cumulative times.</summary>
    public List<string> RaceOrder => CumulativeTimes
        .OrderBy(kv => kv.Value)
        .Select(kv => kv.Key)
        .ToList();

    /// <summary>Applies a race event to mutate state. All domain state changes flow through this method.</summary>
    public IRaceEvent Apply(IRaceEvent evt) {
        Timestamp = evt.Timestamp;

        switch (evt) {
            case RaceStarted:
                break;

            case GridFormed e:
                Cars            = e.Cars.ToDictionary(c => c.Id);
                Circuit         = e.Circuit;
                Conditions      = e.Conditions;
                TotalLaps       = e.TotalLaps;
                Profile         = DifficultyProfile.For(e.Circuit.Difficulty, e.Conditions);
                CumulativeTimes = e.Cars.ToDictionary(c => c.Id, _ => 0.0);
                LapsSincePit    = e.Cars.ToDictionary(c => c.Id, _ => 0);
                Retired         = [];
                break;

            case PitStopPerformed e:
                CumulativeTimes[e.CarId] += e.TimePenalty;
                LapsSincePit[e.CarId]    =  0;
                break;

            case LapCompleted e:
                CumulativeTimes[e.CarId] = e.CumulativeTime;
                LapsSincePit[e.CarId]++;
                Lap = e.Lap;
                break;

            case WeatherChanged e:
                Conditions = new RaceConditions { TimeOfDay = Conditions.TimeOfDay, Weather = e.NewWeather };
                Profile    = DifficultyProfile.For(Circuit.Difficulty, Conditions);
                break;

            case IncidentOccurred e:
                CumulativeTimes[e.CarId] += e.TimePenalty;
                break;

            case CarRetired e:
                Retired.Add(e.CarId);
                CumulativeTimes.Remove(e.CarId);
                LapsSincePit.Remove(e.CarId);
                break;
        }

        return evt;
    }
}
