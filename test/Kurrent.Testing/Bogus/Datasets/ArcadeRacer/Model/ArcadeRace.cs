using System.Collections.Immutable;
using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

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
                _lapsSincePit[car.Id]    =  0;

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
        var timePenalty = dnf ? 0.0 : 2.0 + _faker.Random.Double(0, 5.0);
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