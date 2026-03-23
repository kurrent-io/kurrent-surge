using Bogus;
using Testing.Datasets.ArcadeRacer.Contracts;

namespace Testing.Datasets.ArcadeRacer.Model;

/// <summary>
/// Aggregate that drives a single arcade race simulation. Tracks positions,
/// cumulative times, pit stops, incidents, and weather changes lap by lap.
/// </summary>
class ArcadeRace {
    readonly Faker _faker;

    internal ArcadeRace(Faker faker) {
        _faker = faker;
        State  = new ArcadeRaceState();
    }

    /// <summary>The current mutable race state.</summary>
    public ArcadeRaceState State { get; }

    double ComputeLapTime(RaceCar car, int lapsSincePit) {
        var spec    = car.Spec;
        var profile = State.Profile;

        var baseLapTime = State.Circuit.LengthKm * 20.0;

        var topSpeedFactor = 1.0 - (spec.TopSpeed - 5) * 0.015 * profile.TopSpeedEffectiveness;
        var handlingFactor = 1.0 - (spec.Handling - 5) * 0.015 * profile.HandlingEffectiveness;
        var accelFactor    = 1.0 - (spec.Acceleration - 5) * 0.008;

        var totalSectors   = (double)(State.Circuit.Corners + State.Circuit.Straights);
        var cornerWeight   = State.Circuit.Corners / totalSectors;
        var straightWeight = State.Circuit.Straights / totalSectors;
        var sectorFactor   = cornerWeight * handlingFactor + straightWeight * topSpeedFactor;

        var degradationRate   = (10.0 - spec.Endurance) * 0.003;
        var degradationFactor = 1.0 + degradationRate * lapsSincePit;

        var variance = _faker.Random.Double(-0.3, 0.3);

        return baseLapTime * sectorFactor * accelFactor * degradationFactor * profile.LapTimeModifier + variance;
    }

    #region Commands

    /// <summary>Forms the starting grid from the given options. Must be called before <see cref="Start"/>.</summary>
    /// <param name="options">Race configuration including cars, circuit, and conditions.</param>
    /// <returns>A single-element array containing the <see cref="GridFormed"/> event.</returns>
    public IRaceEvent[] FormGrid(ArcadeRaceOptions options) {
        var e = new GridFormed {
            EventId    = Guid.NewGuid(),
            Cars       = options.Cars.ToArray(),
            Circuit    = options.Circuit,
            Conditions = options.Conditions,
            TotalLaps  = options.TotalLaps,
            Timestamp  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return [State.Apply(e)];
    }

    /// <summary>Starts the race, establishing the race clock.</summary>
    /// <returns>A single-element array containing the <see cref="RaceStarted"/> event.</returns>
    public IRaceEvent[] Start() {
        var e = new RaceStarted {
            EventId   = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return [State.Apply(e)];
    }

    /// <summary>Randomly toggles weather between sunny and rainy based on the difficulty profile.</summary>
    /// <returns>A <see cref="WeatherChanged"/> event if weather changed; otherwise empty.</returns>
    public IRaceEvent[] CheckWeather() {
        if (!_faker.Random.Bool((float)State.Profile.WeatherChangeRate))
            return [];

        var oldWeather = State.Conditions.Weather;
        var newWeather = oldWeather == Weather.Sunny ? Weather.Rainy : Weather.Sunny;

        var e = new WeatherChanged {
            EventId    = Guid.NewGuid(),
            OldWeather = oldWeather,
            NewWeather = newWeather,
            Lap        = State.Lap,
            Timestamp  = State.Timestamp
        };

        return [State.Apply(e)];
    }

    /// <summary>
    /// Simulates a single lap: processes pit stops, computes lap times, updates positions,
    /// and detects overtakes. Lap counter and timestamp are derived from emitted events.
    /// </summary>
    /// <returns>Events produced during the lap (lap completions, pit stops, overtakes).</returns>
    public IRaceEvent[] SimulateLap() {
        var nextTimestamp = State.Timestamp + _faker.Random.Long(85000, 110000);
        var currentLap   = State.Lap + 1;

        var events   = new List<IRaceEvent>();
        var oldOrder = State.RaceOrder;

        ProcessPitStops();
        var lapTimes = ComputeLapTimes();
        EmitLapCompleted(lapTimes);
        DetectOvertakes();

        return events.ToArray();

        void ProcessPitStops() {
            foreach (var carId in State.RaceOrder) {
                var car          = State.Cars[carId];
                var lapsSincePit = State.LapsSincePit[carId];
                var pitWindow    = car.Spec.Endurance * 5 + _faker.Random.Int(-2, 2);

                if (lapsSincePit < pitWindow || lapsSincePit <= 0) continue;

                var e = new PitStopPerformed {
                    EventId     = Guid.NewGuid(),
                    CarId       = carId,
                    Lap         = currentLap,
                    TimePenalty = Math.Round(20.0 + _faker.Random.Double(0, 5.0), 3),
                    Timestamp   = nextTimestamp
                };

                State.Apply(e);
                events.Add(e);
            }
        }

        Dictionary<Guid, double> ComputeLapTimes() {
            var times = new Dictionary<Guid, double>();

            foreach (var carId in State.RaceOrder)
                times[carId] = ComputeLapTime(State.Cars[carId], State.LapsSincePit[carId]);

            return times;
        }

        void EmitLapCompleted(Dictionary<Guid, double> laps) {
            foreach (var carId in State.RaceOrder) {
                var cumTime = State.CumulativeTimes[carId] + laps[carId];

                var e = new LapCompleted {
                    EventId        = Guid.NewGuid(),
                    CarId          = carId,
                    Lap            = currentLap,
                    LapTime        = Math.Round(laps[carId], 3),
                    CumulativeTime = Math.Round(cumTime, 3),
                    Timestamp      = nextTimestamp
                };

                State.Apply(e);
                events.Add(e);
            }
        }

        void DetectOvertakes() {
            var newOrder = State.RaceOrder;

            for (var newPos = 0; newPos < newOrder.Count; newPos++) {
                var carId  = newOrder[newPos];
                var oldPos = oldOrder.IndexOf(carId);

                if (newPos >= oldPos) continue;

                for (var i = newPos; i < oldPos; i++) {
                    events.Add(
                        new OvertakePerformed {
                            EventId     = Guid.NewGuid(),
                            OvertakerId = carId,
                            DefenderId  = oldOrder[i],
                            Lap         = currentLap,
                            Timestamp   = nextTimestamp
                        }
                    );
                }
            }
        }
    }

    /// <summary>Randomly triggers an incident (time penalty or DNF) based on the difficulty profile.</summary>
    /// <returns>An <see cref="IncidentOccurred"/> or <see cref="CarRetired"/> event if triggered; otherwise empty.</returns>
    public IRaceEvent[] CheckIncident() {
        if (!_faker.Random.Bool((float)State.Profile.IncidentRate) || !State.IsActive || State.RaceOrder.Count <= 1)
            return [];

        var carId       = _faker.PickRandom(State.RaceOrder);
        var dnf         = _faker.Random.Bool((float)State.Profile.DnfRate);
        var description = _faker.PickRandom(ArcadeRacerDataSet.IncidentDescriptions);

        if (dnf) {
            var e = new CarRetired {
                EventId     = Guid.NewGuid(),
                CarId       = carId,
                Lap         = State.Lap,
                Description = description,
                Timestamp   = State.Timestamp
            };

            return [State.Apply(e)];
        }
        else {
            var e = new IncidentOccurred {
                EventId     = Guid.NewGuid(),
                CarId       = carId,
                Lap         = State.Lap,
                Description = description,
                TimePenalty = Math.Round(2.0 + _faker.Random.Double(0, 5.0), 3),
                Timestamp   = State.Timestamp
            };

            return [State.Apply(e)];
        }
    }

    /// <summary>Produces the final <see cref="RaceFinished"/> event with ranked results for all cars.</summary>
    /// <returns>A single-element array containing the finish event, or empty if no cars entered.</returns>
    public IRaceEvent[] Finish() {
        if (State.Cars.Count == 0)
            return [];

        var results   = new List<RaceResult>();
        var raceOrder = State.RaceOrder;

        for (var pos = 0; pos < raceOrder.Count; pos++) {
            var carId      = raceOrder[pos];
            var car        = State.Cars[carId];
            var cumTime    = State.CumulativeTimes[carId];
            var leaderTime = State.CumulativeTimes[raceOrder[0]];

            results.Add(
                new RaceResult(
                    car.Id,
                    car.Name,
                    car.Pilot,
                    pos + 1,
                    false,
                    Math.Round(cumTime, 3),
                    Math.Round(cumTime - leaderTime, 3),
                    State.Lap
                )
            );
        }

        var dnfPosition = raceOrder.Count + 1;

        foreach (var carId in State.Retired)
            results.Add(
                new RaceResult(
                    State.Cars[carId].Id,
                    State.Cars[carId].Name,
                    State.Cars[carId].Pilot,
                    dnfPosition++,
                    true,
                    0,
                    0,
                    0
                )
            );

        return [
            new RaceFinished {
                EventId   = Guid.NewGuid(),
                Results   = results.ToArray(),
                TotalLaps = State.Lap,
                Timestamp = State.Timestamp
            }
        ];
    }

    #endregion
}
