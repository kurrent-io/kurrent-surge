// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Immutable;
using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Immutable snapshot of the race state at a point in time.</summary>
public record ArcadeRaceState {
    /// <summary>All cars entered in the race, including DNFs.</summary>
    public ImmutableList<Car>                Cars            { get; init; } = ImmutableList<Car>.Empty;
    /// <summary>Cars still racing, sorted by cumulative time (leader first).</summary>
    public ImmutableList<Car>                RaceOrder       { get; init; } = ImmutableList<Car>.Empty;
    /// <summary>Total elapsed race time per car, keyed by <see cref="Car.Id"/>.</summary>
    public ImmutableDictionary<Guid, double> CumulativeTimes { get; init; } = ImmutableDictionary<Guid, double>.Empty;
    /// <summary>Number of laps completed since the last pit stop, keyed by <see cref="Car.Id"/>.</summary>
    public ImmutableDictionary<Guid, int>    LapsSincePit    { get; init; } = ImmutableDictionary<Guid, int>.Empty;
    /// <summary>The circuit being raced on.</summary>
    public Circuit                           Circuit         { get; init; } = new();
    /// <summary>Current weather and time-of-day conditions.</summary>
    public RaceConditions                    Conditions      { get; init; }
    /// <summary>Active difficulty profile derived from circuit difficulty and conditions.</summary>
    public DifficultyProfile                 Profile         { get; init; } = new();
    /// <summary>Current lap number (1-based).</summary>
    public int                               Lap             { get; init; } = 1;
    /// <summary>Total number of laps in the race. Zero means unlimited.</summary>
    public int                               TotalLaps       { get; init; }
    /// <summary>Unix timestamp in milliseconds for this state snapshot.</summary>
    public long                              Timestamp       { get; init; }

    /// <summary>True if at least one car is still racing.</summary>
    public bool IsActive => RaceOrder.Count > 0;
}

/// <summary>
/// Mutable aggregate that drives a single arcade race simulation. Tracks positions,
/// cumulative times, pit stops, incidents, and weather changes lap by lap.
/// </summary>
internal class ArcadeRace {
    readonly Faker                    _faker;
    readonly List<Car>                _cars;
    readonly List<Car>                _raceOrder;
    readonly Dictionary<Guid, double> _cumulativeTimes;
    readonly Dictionary<Guid, int>    _lapsSincePit;
    readonly Circuit                  _circuit;
    readonly int                      _totalLaps;
    RaceConditions                    _conditions;
    DifficultyProfile                 _profile;
    int                               _lap = 1;
    long                              _timestamp;

    ArcadeRace(Faker faker, ArcadeRaceOptions options) {
        _faker           = faker;
        _cars            = options.Cars.ToList();
        _raceOrder       = options.Cars.ToList();
        _cumulativeTimes = options.Cars.ToDictionary(c => c.Id, _ => 0.0);
        _lapsSincePit    = options.Cars.ToDictionary(c => c.Id, _ => 0);
        _circuit         = options.Circuit;
        _conditions      = options.Conditions;
        _profile         = DifficultyProfile.For(options.Circuit.Difficulty, options.Conditions);
        _totalLaps       = options.TotalLaps;
        _timestamp       = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Projects the current mutable state into an immutable <see cref="ArcadeRaceState"/> snapshot.</summary>
    public ArcadeRaceState State => new() {
        Cars            = _cars.ToImmutableList(),
        RaceOrder       = _raceOrder.ToImmutableList(),
        CumulativeTimes = _cumulativeTimes.ToImmutableDictionary(),
        LapsSincePit    = _lapsSincePit.ToImmutableDictionary(),
        Circuit         = _circuit,
        Conditions      = _conditions,
        Profile         = _profile,
        Lap             = _lap,
        TotalLaps       = _totalLaps,
        Timestamp       = _timestamp
    };

    /// <summary>True if at least one car is still racing.</summary>
    public bool IsActive => _raceOrder.Count > 0;
    /// <summary>Current lap number (1-based).</summary>
    public int  Lap       => _lap;
    /// <summary>Total number of laps configured for this race.</summary>
    public int  TotalLaps => _totalLaps;

    /// <summary>Creates a new race with the starting grid formed from the given options.</summary>
    /// <param name="options">Race configuration including cars, circuit, and conditions.</param>
    /// <param name="faker">Randomizer used for stochastic simulation decisions.</param>
    /// <returns>A new <see cref="ArcadeRace"/> ready to start.</returns>
    public static ArcadeRace FormGrid(ArcadeRaceOptions options, Faker faker) =>
        new(faker, options);

    #region Commands

    /// <summary>Emits the <see cref="RaceStarted"/> event with grid and circuit info.</summary>
    /// <returns>A single-element array containing the start event.</returns>
    public IRaceEvent[] Start() => [
        new RaceStarted {
            EventId    = Guid.NewGuid(),
            Circuit    = _circuit,
            Conditions = _conditions,
            Cars       = _cars.Select(c => c.ToInfo()).ToArray(),
            TotalLaps  = _totalLaps,
            Timestamp  = _timestamp
        }
    ];

    /// <summary>Randomly toggles weather between sunny and rainy based on the difficulty profile.</summary>
    /// <returns>A <see cref="WeatherChanged"/> event if weather changed; otherwise empty.</returns>
    public IRaceEvent[] CheckWeather() {
        if (!_faker.Random.Bool((float)_profile.WeatherChangeRate))
            return [];

        var oldWeather = _conditions.Weather;
        var newWeather = oldWeather == Weather.Sunny ? Weather.Rainy : Weather.Sunny;

        _conditions = _conditions with { Weather = newWeather };
        _profile    = DifficultyProfile.For(_circuit.Difficulty, _conditions);

        return [
            new WeatherChanged {
                EventId    = Guid.NewGuid(),
                OldWeather = oldWeather,
                NewWeather = newWeather,
                Lap        = _lap,
                Timestamp  = _timestamp
            }
        ];
    }

    /// <summary>
    /// Simulates a single lap: processes pit stops, computes lap times, updates positions,
    /// and detects overtakes. Advances the internal timestamp and lap counter.
    /// </summary>
    /// <returns>Events produced during the lap (lap completions, pit stops, overtakes).</returns>
    public IRaceEvent[] SimulateLap() {
        AdvanceTimestamp();

        var events   = new List<IRaceEvent>();
        var oldOrder = _raceOrder.Select(c => c.Id).ToList();
        var lapTimes = new Dictionary<Guid, double>();

        ProcessPitStops();
        ComputeAndApplyLapTimes();
        UpdatePositions();
        EmitLapCompleted();
        DetectOvertakes();

        IncrementLap();

        return events.ToArray();

        void ProcessPitStops() {
            foreach (var car in _raceOrder) {
                var lapsSincePit = _lapsSincePit[car.Id];
                var pitWindow    = car.Spec.Endurance * 5 + _faker.Random.Int(-2, 2);

                if (lapsSincePit < pitWindow || lapsSincePit <= 0) continue;

                var enduranceBefore = ComputeEnduranceRemaining(car);
                var timePenalty     = 20.0 + _faker.Random.Double(0, 5.0);

                _cumulativeTimes[car.Id] += timePenalty;
                _lapsSincePit[car.Id]     = 0;

                events.Add(new PitStopPerformed {
                    EventId         = Guid.NewGuid(),
                    CarId           = car.Id,
                    Pilot           = car.Pilot,
                    Lap             = _lap,
                    TimePenalty     = Math.Round(timePenalty, 3),
                    EnduranceBefore = Math.Round(enduranceBefore, 3),
                    EnduranceAfter  = 1.0,
                    Timestamp       = _timestamp
                });
            }
        }

        void ComputeAndApplyLapTimes() {
            foreach (var car in _raceOrder) {
                var lapTime = ComputeLapTime(car, _lapsSincePit[car.Id]);
                _cumulativeTimes[car.Id] += lapTime;
                _lapsSincePit[car.Id]++;
                lapTimes[car.Id] = lapTime;
            }
        }

        void UpdatePositions() =>
            _raceOrder.Sort((a, b) => _cumulativeTimes[a.Id].CompareTo(_cumulativeTimes[b.Id]));

        void EmitLapCompleted() {
            var leaderTime = _cumulativeTimes[_raceOrder[0].Id];

            for (var pos = 0; pos < _raceOrder.Count; pos++) {
                var car     = _raceOrder[pos];
                var cumTime = _cumulativeTimes[car.Id];

                events.Add(new LapCompleted {
                    EventId            = Guid.NewGuid(),
                    CarId              = car.Id,
                    CarName            = car.Name,
                    Pilot              = car.Pilot,
                    Lap                = _lap,
                    Position           = pos + 1,
                    LapTime            = Math.Round(lapTimes[car.Id], 3),
                    CumulativeTime     = Math.Round(cumTime, 3),
                    GapToLeader        = Math.Round(cumTime - leaderTime, 3),
                    EnduranceRemaining = Math.Round(ComputeEnduranceRemaining(car), 3),
                    Timestamp          = _timestamp
                });
            }
        }

        void DetectOvertakes() {
            var newOrder = _raceOrder.Select(c => c.Id).ToList();

            for (var newPos = 0; newPos < newOrder.Count; newPos++) {
                var carId  = newOrder[newPos];
                var oldPos = oldOrder.IndexOf(carId);

                if (newPos >= oldPos) continue;

                var car = _raceOrder[newPos];

                for (var i = newPos; i < oldPos; i++) {
                    var passedId = oldOrder[i];
                    var passed   = _cars.First(c => c.Id == passedId);

                    events.Add(new OvertakePerformed {
                        EventId     = Guid.NewGuid(),
                        OvertakerId = car.Id,
                        DefenderId  = passedId,
                        Overtaker   = car.Pilot,
                        Defender    = passed.Pilot,
                        Lap         = _lap,
                        Timestamp   = _timestamp
                    });
                }
            }
        }
    }

    /// <summary>Randomly triggers an incident (time penalty or DNF) based on the difficulty profile.</summary>
    /// <returns>An <see cref="IncidentOccurred"/> event if triggered; otherwise empty.</returns>
    public IRaceEvent[] CheckIncident() {
        if (!_faker.Random.Bool((float)_profile.IncidentRate) || _raceOrder.Count == 0)
            return [];

        var car         = _faker.PickRandom(_raceOrder);
        var dnf         = _faker.Random.Bool((float)_profile.DnfRate);
        var timePenalty  = dnf ? 0.0 : 2.0 + _faker.Random.Double(0, 5.0);
        var description = _faker.PickRandom(ArcadeRacerDataSet.IncidentDescriptions);

        if (!dnf)
            _cumulativeTimes[car.Id] += timePenalty;

        var incident = new IncidentOccurred {
            EventId     = Guid.NewGuid(),
            CarId       = car.Id,
            Pilot       = car.Pilot,
            Lap         = _lap,
            Description = description,
            Dnf         = dnf,
            TimePenalty = Math.Round(timePenalty, 3),
            Timestamp   = _timestamp
        };

        if (dnf) {
            car.Status = CarStatus.DidNotFinish;
            _raceOrder.Remove(car);
            _cumulativeTimes.Remove(car.Id);
            _lapsSincePit.Remove(car.Id);
        }

        return [incident];
    }

    /// <summary>Produces the final <see cref="RaceFinished"/> event with ranked results for all cars.</summary>
    /// <returns>A single-element array containing the finish event, or empty if no cars entered.</returns>
    public IRaceEvent[] Finish() {
        if (_cars.Count == 0)
            return [];

        var results = new List<RaceResult>();

        for (var pos = 0; pos < _raceOrder.Count; pos++) {
            var car        = _raceOrder[pos];
            var cumTime    = _cumulativeTimes[car.Id];
            var leaderTime = _cumulativeTimes[_raceOrder[0].Id];

            results.Add(new RaceResult(
                CarId:          car.Id,
                CarName:        car.Name,
                Pilot:          car.Pilot,
                Position:       pos + 1,
                Dnf:            false,
                CumulativeTime: Math.Round(cumTime, 3),
                GapToLeader:    Math.Round(cumTime - leaderTime, 3),
                LapsCompleted:  _lap
            ));
        }

        var dnfPosition = _raceOrder.Count + 1;

        foreach (var car in _cars.Where(c => c.Status == CarStatus.DidNotFinish)) {
            results.Add(new RaceResult(
                CarId:          car.Id,
                CarName:        car.Name,
                Pilot:          car.Pilot,
                Position:       dnfPosition++,
                Dnf:            true,
                CumulativeTime: 0,
                GapToLeader:    0,
                LapsCompleted:  0
            ));
        }

        return [
            new RaceFinished {
                EventId   = Guid.NewGuid(),
                Results   = results.ToArray(),
                TotalLaps = _lap,
                Timestamp = _timestamp
            }
        ];
    }

    #endregion

    void AdvanceTimestamp() =>
        _timestamp += _faker.Random.Long(85000, 110000);

    void IncrementLap() =>
        _lap++;

    double ComputeLapTime(Car car, int lapsSincePit) {
        var spec    = car.Spec;
        var profile = _profile;

        var baseLapTime = _circuit.LengthKm * 20.0;

        var topSpeedFactor = 1.0 - (spec.TopSpeed - 5) * 0.015 * profile.TopSpeedEffectiveness;
        var handlingFactor = 1.0 - (spec.Handling - 5) * 0.015 * profile.HandlingEffectiveness;
        var accelFactor    = 1.0 - (spec.Acceleration - 5) * 0.008;

        var totalSectors   = (double)(_circuit.Corners + _circuit.Straights);
        var cornerWeight   = _circuit.Corners / totalSectors;
        var straightWeight = _circuit.Straights / totalSectors;
        var sectorFactor   = cornerWeight * handlingFactor + straightWeight * topSpeedFactor;

        var degradationRate   = (10.0 - spec.Endurance) * 0.003;
        var degradationFactor = 1.0 + degradationRate * lapsSincePit;

        var variance = _faker.Random.Double(-0.3, 0.3);

        return baseLapTime * sectorFactor * accelFactor * degradationFactor * profile.LapTimeModifier + variance;
    }

    double ComputeEnduranceRemaining(Car car) {
        var lapsSincePit = _lapsSincePit[car.Id];
        var pitWindow    = car.Spec.Endurance * 5.0;

        return Math.Max(0.0, 1.0 - lapsSincePit / pitWindow);
    }
}

/// <summary>Configuration used to initialize an <see cref="ArcadeRace"/>.</summary>
internal record ArcadeRaceOptions {
    /// <summary>Cars on the starting grid.</summary>
    public List<Car>      Cars       { get; init; } = [];
    /// <summary>Circuit to race on.</summary>
    public Circuit        Circuit    { get; init; } = new();
    /// <summary>Initial weather and time-of-day conditions.</summary>
    public RaceConditions Conditions { get; init; } = RaceConditions.Default;
    /// <summary>Number of laps. Zero means unlimited (streaming mode only).</summary>
    public int            TotalLaps  { get; init; }
}


/// <summary>Time of day affecting visibility and handling effectiveness.</summary>
public enum TimeOfDay { Unspecified, Day, Night }

/// <summary>Weather condition affecting grip and top speed effectiveness.</summary>
public enum Weather { Unspecified, Sunny, Rainy }

/// <summary>Circuit difficulty tier influencing incident rates and lap time modifiers.</summary>
public enum CircuitDifficulty { Easy, Medium, Hard }

/// <summary>Whether a car is actively racing or has been eliminated.</summary>
public enum CarStatus { Racing, DidNotFinish }

/// <summary>Performance attributes for a car. Each stat ranges from 1 (worst) to 10 (best).</summary>
/// <param name="Acceleration">Affects lap time via acceleration factor.</param>
/// <param name="TopSpeed">Affects lap time on straights; reduced in rain.</param>
/// <param name="Handling">Affects lap time in corners; reduced at night.</param>
/// <param name="Endurance">Controls tire degradation rate and pit stop window.</param>
public record struct CarSpec(int Acceleration, int TopSpeed, int Handling, int Endurance) {
    /// <summary>Neutral baseline spec with all stats set to 5.</summary>
    public static readonly CarSpec Default = new(5, 5, 5, 5);
}

/// <summary>Environmental conditions that affect the <see cref="DifficultyProfile"/>.</summary>
/// <param name="TimeOfDay">Time of day (day or night).</param>
/// <param name="Weather">Weather condition (sunny or rainy).</param>
public record struct RaceConditions(TimeOfDay TimeOfDay, Weather Weather) {
    /// <summary>Sentinel value indicating conditions have not been set.</summary>
    public static readonly RaceConditions Unspecified = new(TimeOfDay.Unspecified, Weather.Unspecified);
    /// <summary>Standard conditions: daytime, sunny.</summary>
    public static readonly RaceConditions Default     = new(TimeOfDay.Day, Weather.Sunny);
}

/// <summary>Lightweight read-only projection of a <see cref="Car"/> for use in events.</summary>
/// <param name="CarId">Unique car identifier.</param>
/// <param name="Name">Generated car name.</param>
/// <param name="Number">Grid number.</param>
/// <param name="Pilot">Pilot name in abbreviated form (e.g. "J. Smith").</param>
/// <param name="Spec">Car performance attributes.</param>
public record struct CarInfo(Guid CarId, string Name, int Number, string Pilot, CarSpec Spec);

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

/// <summary>Simulation tuning parameters derived from <see cref="CircuitDifficulty"/> and <see cref="RaceConditions"/>.</summary>
public record DifficultyProfile {
    /// <summary>Multiplier for handling stat effectiveness. Reduced at night.</summary>
    public double HandlingEffectiveness { get; init; } = 1.0;
    /// <summary>Multiplier for top speed stat effectiveness. Reduced in rain.</summary>
    public double TopSpeedEffectiveness { get; init; } = 1.0;
    /// <summary>Per-lap probability of an incident occurring (0.0 to 1.0).</summary>
    public double IncidentRate          { get; init; } = 0.05;
    /// <summary>Probability that an incident results in a DNF (0.0 to 1.0).</summary>
    public double DnfRate               { get; init; } = 0.30;
    /// <summary>Per-lap probability of a weather change (0.0 to 1.0).</summary>
    public double WeatherChangeRate     { get; init; } = 0.03;
    /// <summary>Scalar applied to all computed lap times. Higher values slow the field.</summary>
    public double LapTimeModifier       { get; init; } = 1.0;

    /// <summary>Computes a profile from circuit difficulty and current conditions.</summary>
    /// <param name="difficulty">Circuit difficulty tier.</param>
    /// <param name="conditions">Current weather and time-of-day.</param>
    /// <returns>A tuned <see cref="DifficultyProfile"/>.</returns>
    public static DifficultyProfile For(CircuitDifficulty difficulty, RaceConditions conditions) {
        var nightMul = conditions.TimeOfDay == TimeOfDay.Night ? 1.5 : 1.0;
        var rainMul  = conditions.Weather == Weather.Rainy ? 1.8 : 1.0;

        return new DifficultyProfile {
            HandlingEffectiveness = conditions.TimeOfDay == TimeOfDay.Night ? 0.8 : 1.0,
            TopSpeedEffectiveness = conditions.Weather == Weather.Rainy ? 0.7 : 1.0,
            IncidentRate = difficulty switch {
                CircuitDifficulty.Easy => 0.02,
                CircuitDifficulty.Hard => 0.08,
                _                      => 0.05
            } * nightMul * rainMul,
            DnfRate = 0.30,
            WeatherChangeRate = difficulty switch {
                CircuitDifficulty.Easy => 0.01f,
                CircuitDifficulty.Hard => 0.05f,
                _                      => 0.03f
            },
            LapTimeModifier = difficulty switch {
                CircuitDifficulty.Easy => 0.98,
                CircuitDifficulty.Hard => 1.02,
                _                      => 1.0
            }
        };
    }
}

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

/// <summary>A racing circuit with layout characteristics that influence lap time calculations.</summary>
public class Circuit {
    /// <summary>Unique identifier for the circuit.</summary>
    public Guid              Id         { get; set; } = Guid.Empty;
    /// <summary>Generated circuit name (e.g. "Silvervale").</summary>
    public string            Name       { get; set; } = "";
    /// <summary>Country where the circuit is located.</summary>
    public string            Country    { get; set; } = "";
    /// <summary>Number of corners. Higher values favor cars with better handling.</summary>
    public int               Corners    { get; set; }
    /// <summary>Number of straights. Higher values favor cars with better top speed.</summary>
    public int               Straights  { get; set; }
    /// <summary>Track length in kilometers. Scales the base lap time.</summary>
    public double            LengthKm   { get; set; }
    /// <summary>Difficulty tier affecting incident rates and lap time modifiers.</summary>
    public CircuitDifficulty Difficulty { get; set; } = CircuitDifficulty.Medium;
}
