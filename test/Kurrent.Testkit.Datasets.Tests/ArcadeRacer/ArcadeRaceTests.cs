// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.Tests.ArcadeRacer;

[Category("ArcadeRacer")]
public class ArcadeRaceTests {

    static ArcadeRace CreateRace(int cars = 4, int laps = 10, CircuitDifficulty difficulty = CircuitDifficulty.Medium) {
        var faker = new Faker();

        var ds    = new ArcadeRacerDataSet(faker);
        var race  = new ArcadeRace(PunyId.New(), faker);

        race.FormGrid(new ArcadeRaceOptions {
            Cars       = ds.RaceCar.Generate(cars),
            Circuit    = Circuit.New(faker).RuleFor(x => x.Difficulty, difficulty).Generate(),
            Conditions = new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny },
            TotalLaps  = laps
        });

        return race;
    }

    #region ->> Start <<-

    [Test]
    public async Task start_emits_race_started() {
        var race   = CreateRace();
        var events = race.Start();

        await Assert.That(events).Count().IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<RaceStarted>();
    }

    #endregion

    #region ->> SimulateLap <<-

    [Test]
    public async Task simulate_lap_emits_lap_completed_per_car() {
        var race = CreateRace(cars: 4);
        race.Start();

        var events    = race.SimulateLap();
        var lapEvents = events.OfType<LapCompleted>().ToList();

        await Assert.That(lapEvents).Count().IsEqualTo(4);
    }

    [Test]
    public async Task simulate_lap_lap_times_are_positive() {
        var race = CreateRace(cars: 4);
        race.Start();

        var events = race.SimulateLap();

        foreach (var lap in events.OfType<LapCompleted>())
            await Assert.That(lap.LapTime).IsGreaterThan(0.0);
    }

    [Test]
    public async Task higher_handling_car_is_faster_on_high_corner_circuit() {
        var faker   = new Faker();
        var circuit = new Circuit {
            Id = PunyId.New(), Name = "Cornerville", Country = "Test",
            Corners = 16, Straights = 2, LengthKm = 5.0, Difficulty = CircuitDifficulty.Medium
        };

        var highHandling = new RaceCar {
            Id = PunyId.New(), Name = "Handler", Number = 1, Pilot = "H. Test",
            Spec = new RaceCarSpec { Acceleration = 5, TopSpeed = 5, Handling = 10, Endurance = 10 }
        };

        var lowHandling = new RaceCar {
            Id = PunyId.New(), Name = "Speeder", Number = 2, Pilot = "S. Test",
            Spec = new RaceCarSpec { Acceleration = 5, TopSpeed = 5, Handling = 1, Endurance = 10 }
        };

        var race = new ArcadeRace(PunyId.New(), faker);

        race.FormGrid(new ArcadeRaceOptions {
            Cars       = [highHandling, lowHandling],
            Circuit    = circuit,
            Conditions = new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny },
            TotalLaps  = 5
        });

        race.Start();

        // Simulate multiple laps to let the pattern emerge
        for (var i = 0; i < 6; i++)
            race.SimulateLap();

        var state      = race.State;
        var handlerPos = state.RaceOrder.IndexOf(highHandling.Id);
        var speederPos = state.RaceOrder.IndexOf(lowHandling.Id);

        await Assert.That(handlerPos).IsLessThan(speederPos);
    }

    #endregion

    #region ->> CheckIncident <<-

    [Test]
    public async Task check_incident_returns_empty_when_no_incident() {
        // Use easy difficulty with day/sunny for lowest incident rate
        var race = CreateRace(cars: 4, difficulty: CircuitDifficulty.Easy);

        race.Start();

        // Run many times; at least one should return empty
        var emptyCount = 0;

        for (var i = 0; i < 100; i++) {
            var events = race.CheckIncident();
            if (events.Length == 0) emptyCount++;
        }

        await Assert.That(emptyCount).IsGreaterThan(0);
    }

    [Test]
    public async Task car_retired_removes_car_from_race_order() {
        var race = CreateRace(cars: 4);
        race.Start();

        // Force many incident checks until a retirement occurs
        var retirementFound = false;

        for (var i = 0; i < 1000 && !retirementFound; i++) {
            var events  = race.CheckIncident();
            var retired = events.OfType<CarRetired>().FirstOrDefault();

            if (retired is not null) {
                await Assert.That(race.State.RaceOrder.Contains(retired.CarId)).IsFalse();
                retirementFound = true;
            }
        }

        await Assert.That(retirementFound).IsTrue();
    }

    [Test]
    public async Task last_car_standing_cannot_retire() {
        // Start with only 2 cars so one retirement leaves a sole survivor
        var race = CreateRace(cars: 2);
        race.Start();
        race.SimulateLap();

        // Force incidents until one car retires
        for (var i = 0; i < 1000 && race.State.RaceOrder.Count > 1; i++)
            race.CheckIncident();

        await Assert.That(race.State.RaceOrder.Count).IsEqualTo(1);

        // Now hammer incident checks — the last car must survive
        for (var i = 0; i < 1000; i++)
            race.CheckIncident();

        await Assert.That(race.State.RaceOrder.Count).IsEqualTo(1);
        await Assert.That(race.State.IsActive).IsTrue();
    }

    #endregion

    #region ->> CheckWeather <<-

    [Test]
    public async Task check_weather_toggles_between_sunny_and_rainy() {
        // Force weather change by running many checks on hard difficulty
        var race = CreateRace(cars: 2, difficulty: CircuitDifficulty.Hard);
        race.Start();

        WeatherChanged? change = null;

        for (var i = 0; i < 1000 && change is null; i++) {
            var events = race.CheckWeather();
            change = events.OfType<WeatherChanged>().FirstOrDefault();
        }

        await Assert.That(change).IsNotNull();
        await Assert.That(change!.OldWeather).IsNotEqualTo(change.NewWeather);
    }

    #endregion

    #region ->> Finish <<-

    [Test]
    public async Task finish_emits_single_race_finished_event() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var events = race.Finish();

        await Assert.That(events).Count().IsEqualTo(1);
        await Assert.That(events[0]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async Task finish_includes_all_cars_in_results() {
        var race = CreateRace(cars: 6);
        race.Start();
        race.SimulateLap();

        var events   = race.Finish();
        var finished = (RaceFinished)events[0];

        await Assert.That(finished.Results).Count().IsEqualTo(6);
    }

    [Test]
    public async Task finish_positions_are_sequential() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var events    = race.Finish();
        var finished  = (RaceFinished)events[0];
        var positions = finished.Results.Select(r => r.Position).OrderBy(p => p).ToList();

        await Assert.That(positions).IsEquivalentTo([1, 2, 3, 4]);
    }

    [Test]
    public async Task finish_leader_has_zero_gap() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var events   = race.Finish();
        var finished = (RaceFinished)events[0];
        var leader   = finished.Results.First(r => r.Position == 1);

        await Assert.That(leader.GapToLeader).IsEqualTo(0.0);
    }

    #endregion

}
