// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

public record ArcadeRaceState {
    public List<Car>                Cars            { get; init; } = [];
    public List<Car>                RaceOrder       { get; init; } = [];
    public Dictionary<Guid, double> CumulativeTimes { get; init; } = [];
    public Dictionary<Guid, int>   LapsSincePit    { get; init; } = [];
    public Circuit                 Circuit         { get; init; } = new();
    public RaceConditions          Conditions      { get; set; }
    public DifficultyProfile       Profile         { get; set; } = new();
    public int                     Lap             { get; set; } = 1;
    public int                     TotalLaps       { get; init; }
    public long                    Timestamp       { get; set; }

    public bool IsActive => RaceOrder.Count > 0;
}

internal class ArcadeRace {
    readonly Faker _faker;

    ArcadeRaceState State { get; set; }

    ArcadeRace(Faker faker, ArcadeRaceState state) {
        _faker = faker;
        State  = state;
    }

    #region Factory

    public static ArcadeRace Initialize(ArcadeRaceOptions options, Faker faker) {
        var state = new ArcadeRaceState {
            Cars            = options.Cars.ToList(),
            RaceOrder       = options.Cars.ToList(),
            CumulativeTimes = options.Cars.ToDictionary(c => c.Id, _ => 0.0),
            LapsSincePit    = options.Cars.ToDictionary(c => c.Id, _ => 0),
            Circuit         = options.Circuit,
            Conditions      = options.Conditions,
            Profile         = DifficultyProfile.For(options.Circuit.Difficulty, options.Conditions),
            TotalLaps       = options.TotalLaps,
            Timestamp       = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return new ArcadeRace(faker, state);
    }

    #endregion

    #region Internal state mutations

    internal void AdvanceTimestamp() =>
        State.Timestamp += _faker.Random.Long(85000, 110000);

    internal void IncrementLap() =>
        State.Lap++;

    #endregion

    #region Commands

    public (ArcadeRaceState, IRaceEvent[]) Start() =>
        (State, [
            new RaceStarted {
                EventId    = Guid.NewGuid(),
                Circuit    = State.Circuit,
                Conditions = State.Conditions,
                Cars       = State.Cars.Select(c => c.ToInfo()).ToArray(),
                TotalLaps  = State.TotalLaps,
                Timestamp  = State.Timestamp
            }
        ]);

    public (ArcadeRaceState, IRaceEvent[]) CheckWeather() {
        var profile = State.Profile;

        if (!_faker.Random.Bool((float)profile.WeatherChangeRate))
            return (State, []);

        var oldWeather = State.Conditions.Weather;
        var newWeather = oldWeather == Weather.Sunny ? Weather.Rainy : Weather.Sunny;

        State.Conditions = State.Conditions with { Weather = newWeather };
        State.Profile    = DifficultyProfile.For(State.Circuit.Difficulty, State.Conditions);

        return (State, [
            new WeatherChanged {
                EventId    = Guid.NewGuid(),
                OldWeather = oldWeather,
                NewWeather = newWeather,
                Lap        = State.Lap,
                Timestamp  = State.Timestamp
            }
        ]);
    }

    public (ArcadeRaceState, IRaceEvent[]) SimulateLap() {
        var events   = new List<IRaceEvent>();
        var oldOrder = State.RaceOrder.Select(c => c.Id).ToList();
        var lapTimes = new Dictionary<Guid, double>();

        foreach (var car in State.RaceOrder) {
            var lapsSincePit = State.LapsSincePit[car.Id];

            // Pit stop check
            var pitWindow = car.Spec.Endurance * 5 + _faker.Random.Int(-2, 2);

            if (lapsSincePit >= pitWindow && lapsSincePit > 0) {
                var enduranceBefore = ComputeEnduranceRemaining(car);
                var timePenalty     = 20.0 + _faker.Random.Double(0, 5.0);

                State.CumulativeTimes[car.Id] += timePenalty;
                State.LapsSincePit[car.Id]     = 0;

                events.Add(new PitStopPerformed {
                    EventId         = Guid.NewGuid(),
                    CarId           = car.Id,
                    Pilot           = car.Pilot,
                    Lap             = State.Lap,
                    TimePenalty     = Math.Round(timePenalty, 3),
                    EnduranceBefore = Math.Round(enduranceBefore, 3),
                    EnduranceAfter  = 1.0,
                    Timestamp       = State.Timestamp
                });

                lapsSincePit = 0;
            }

            // Compute and apply lap time
            var lapTime = ComputeLapTime(car, lapsSincePit);
            State.CumulativeTimes[car.Id] += lapTime;
            State.LapsSincePit[car.Id]++;
            lapTimes[car.Id] = lapTime;
        }

        // Re-sort by cumulative time
        State.RaceOrder.Sort((a, b) => State.CumulativeTimes[a.Id].CompareTo(State.CumulativeTimes[b.Id]));
        var leaderTime = State.CumulativeTimes[State.RaceOrder[0].Id];

        // Emit LapCompleted per car
        for (var pos = 0; pos < State.RaceOrder.Count; pos++) {
            var car     = State.RaceOrder[pos];
            var cumTime = State.CumulativeTimes[car.Id];

            events.Add(new LapCompleted {
                EventId            = Guid.NewGuid(),
                CarId              = car.Id,
                CarName            = car.Name,
                Pilot              = car.Pilot,
                Lap                = State.Lap,
                Position           = pos + 1,
                LapTime            = Math.Round(lapTimes[car.Id], 3),
                CumulativeTime     = Math.Round(cumTime, 3),
                GapToLeader        = Math.Round(cumTime - leaderTime, 3),
                EnduranceRemaining = Math.Round(ComputeEnduranceRemaining(car), 3),
                Timestamp          = State.Timestamp
            });
        }

        // Detect overtakes
        var newOrder = State.RaceOrder.Select(c => c.Id).ToList();

        for (var newPos = 0; newPos < newOrder.Count; newPos++) {
            var carId  = newOrder[newPos];
            var oldPos = oldOrder.IndexOf(carId);

            if (newPos >= oldPos) continue;

            var car = State.RaceOrder[newPos];

            for (var i = newPos; i < oldPos; i++) {
                var passedId = oldOrder[i];
                var passed   = State.Cars.First(c => c.Id == passedId);

                events.Add(new OvertakePerformed {
                    EventId     = Guid.NewGuid(),
                    OvertakerId = car.Id,
                    DefenderId  = passedId,
                    Overtaker   = car.Pilot,
                    Defender    = passed.Pilot,
                    Lap         = State.Lap,
                    Timestamp   = State.Timestamp
                });
            }
        }

        return (State, events.ToArray());
    }

    public (ArcadeRaceState, IRaceEvent[]) CheckIncident() {
        var profile = State.Profile;

        if (!_faker.Random.Bool((float)profile.IncidentRate) || State.RaceOrder.Count == 0)
            return (State, []);

        var car         = _faker.PickRandom(State.RaceOrder);
        var dnf         = _faker.Random.Bool((float)profile.DnfRate);
        var timePenalty  = dnf ? 0.0 : 2.0 + _faker.Random.Double(0, 5.0);
        var description = _faker.PickRandom(ArcadeRacerDataSet.IncidentDescriptions);

        if (!dnf)
            State.CumulativeTimes[car.Id] += timePenalty;

        var incident = new IncidentOccurred {
            EventId     = Guid.NewGuid(),
            CarId       = car.Id,
            Pilot       = car.Pilot,
            Lap         = State.Lap,
            Description = description,
            Dnf         = dnf,
            TimePenalty = Math.Round(timePenalty, 3),
            Timestamp   = State.Timestamp
        };

        if (dnf) {
            car.Status = CarStatus.DidNotFinish;
            State.RaceOrder.Remove(car);
            State.CumulativeTimes.Remove(car.Id);
            State.LapsSincePit.Remove(car.Id);
        }

        return (State, [incident]);
    }

    public (ArcadeRaceState, IRaceEvent[]) Finish() {
        if (State.Cars.Count == 0)
            return (State, []);

        var results = new List<RaceResult>();

        // Active cars ordered by cumulative time
        for (var pos = 0; pos < State.RaceOrder.Count; pos++) {
            var car        = State.RaceOrder[pos];
            var cumTime    = State.CumulativeTimes[car.Id];
            var leaderTime = State.CumulativeTimes[State.RaceOrder[0].Id];

            results.Add(new RaceResult(
                CarId:          car.Id,
                CarName:        car.Name,
                Pilot:          car.Pilot,
                Position:       pos + 1,
                Dnf:            false,
                CumulativeTime: Math.Round(cumTime, 3),
                GapToLeader:    Math.Round(cumTime - leaderTime, 3),
                LapsCompleted:  State.Lap
            ));
        }

        // DNF cars placed after active cars
        var dnfPosition = State.RaceOrder.Count + 1;

        foreach (var car in State.Cars.Where(c => c.Status == CarStatus.DidNotFinish)) {
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

        return (State, [
            new RaceFinished {
                EventId   = Guid.NewGuid(),
                Results   = results.ToArray(),
                TotalLaps = State.Lap,
                Timestamp = State.Timestamp
            }
        ]);
    }

    #endregion

    #region Lap time computation

    double ComputeLapTime(Car car, int lapsSincePit) {
        var spec    = car.Spec;
        var circuit = State.Circuit;
        var profile = State.Profile;

        var baseLapTime = circuit.LengthKm * 20.0;

        var topSpeedFactor = 1.0 - (spec.TopSpeed - 5) * 0.015 * profile.TopSpeedEffectiveness;
        var handlingFactor = 1.0 - (spec.Handling - 5) * 0.015 * profile.HandlingEffectiveness;
        var accelFactor    = 1.0 - (spec.Acceleration - 5) * 0.008;

        var totalSectors   = (double)(circuit.Corners + circuit.Straights);
        var cornerWeight   = circuit.Corners / totalSectors;
        var straightWeight = circuit.Straights / totalSectors;
        var sectorFactor   = cornerWeight * handlingFactor + straightWeight * topSpeedFactor;

        var degradationRate   = (10.0 - spec.Endurance) * 0.003;
        var degradationFactor = 1.0 + degradationRate * lapsSincePit;

        var variance = _faker.Random.Double(-0.3, 0.3);

        return baseLapTime * sectorFactor * accelFactor * degradationFactor * profile.LapTimeModifier + variance;
    }

    double ComputeEnduranceRemaining(Car car) {
        var lapsSincePit = State.LapsSincePit[car.Id];
        var pitWindow    = car.Spec.Endurance * 5.0;

        return Math.Max(0.0, 1.0 - lapsSincePit / pitWindow);
    }

    #endregion
}

internal record ArcadeRaceOptions {
    public List<Car>      Cars       { get; init; } = [];
    public Circuit        Circuit    { get; init; } = new();
    public RaceConditions Conditions { get; init; } = RaceConditions.Default;
    public int            TotalLaps  { get; init; }
}
