// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer.Tests;

[Category("ArcadeRacer")]
public class ArcadeRaceTests {

    static ArcadeRace CreateRace(int cars = 4, int laps = 10, CircuitDifficulty difficulty = CircuitDifficulty.Medium) {
        var faker = new Faker();
        var ds = new ArcadeRacerDataSet(faker);

        return ArcadeRace.Initialize(new ArcadeRaceOptions {
            Cars       = ds.Grid(cars),
            Circuit    = ds.Circuit(difficulty),
            Conditions = new RaceConditions(TimeOfDay.Day, Weather.Sunny),
            TotalLaps  = laps
        }, faker);
    }

    #region Start

    [Test]
    public async Task start_emits_race_started() {
        var race = CreateRace();
        var (state, events) = race.Start();

        await Assert.That(events).HasCount().EqualTo(1);
        await Assert.That(events[0]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async Task start_includes_all_cars() {
        var race = CreateRace(cars: 6);
        var (_, events) = race.Start();
        var started = (RaceStarted)events[0];

        await Assert.That(started.Cars).HasCount().EqualTo(6);
    }

    [Test]
    public async Task start_includes_circuit_and_conditions() {
        var race = CreateRace();
        var (_, events) = race.Start();
        var started = (RaceStarted)events[0];

        await Assert.That(started.Circuit.Name).IsNotEmpty();
        await Assert.That(started.Conditions.TimeOfDay).IsEqualTo(TimeOfDay.Day);
        await Assert.That(started.Conditions.Weather).IsEqualTo(Weather.Sunny);
    }

    #endregion

    #region SimulateLap

    [Test]
    public async Task simulate_lap_emits_lap_completed_per_car() {
        var race = CreateRace(cars: 4);
        race.Start();

        var (_, events) = race.SimulateLap();
        var lapEvents = events.OfType<LapCompleted>().ToList();

        await Assert.That(lapEvents).HasCount().EqualTo(4);
    }

    [Test]
    public async Task simulate_lap_positions_are_sequential() {
        var race = CreateRace(cars: 6);
        race.Start();

        var (_, events) = race.SimulateLap();
        var positions = events.OfType<LapCompleted>().Select(e => e.Position).OrderBy(p => p).ToList();

        await Assert.That(positions).IsEquivalentTo([1, 2, 3, 4, 5, 6]);
    }

    [Test]
    public async Task simulate_lap_leader_has_zero_gap() {
        var race = CreateRace(cars: 4);
        race.Start();

        var (_, events) = race.SimulateLap();
        var leader = events.OfType<LapCompleted>().First(e => e.Position == 1);

        await Assert.That(leader.GapToLeader).IsEqualTo(0.0);
    }

    [Test]
    public async Task simulate_lap_lap_times_are_positive() {
        var race = CreateRace(cars: 4);
        race.Start();

        var (_, events) = race.SimulateLap();

        foreach (var lap in events.OfType<LapCompleted>())
            await Assert.That(lap.LapTime).IsGreaterThan(0.0);
    }

    [Test]
    public async Task higher_handling_car_is_faster_on_high_corner_circuit() {
        var faker   = new Faker();
        var circuit = new Circuit {
            Id = Guid.NewGuid(), Name = "Cornerville", Country = "Test",
            Corners = 16, Straights = 2, LengthKm = 5.0, Difficulty = CircuitDifficulty.Medium
        };

        var highHandling = new Car {
            Id = Guid.NewGuid(), Name = "Handler", Number = 1, Pilot = "H. Test",
            Spec = new CarSpec(5, 5, 10, 10)
        };

        var lowHandling = new Car {
            Id = Guid.NewGuid(), Name = "Speeder", Number = 2, Pilot = "S. Test",
            Spec = new CarSpec(5, 5, 1, 10)
        };

        var race = ArcadeRace.Initialize(new ArcadeRaceOptions {
            Cars       = [highHandling, lowHandling],
            Circuit    = circuit,
            Conditions = new RaceConditions(TimeOfDay.Day, Weather.Sunny),
            TotalLaps  = 5
        }, faker);

        race.Start();

        // Simulate multiple laps to let the pattern emerge
        for (var i = 0; i < 5; i++) {
            race.SimulateLap();
            race.IncrementLap();
        }

        var (state, _) = race.SimulateLap();
        var handlerPos = state.RaceOrder.FindIndex(c => c.Id == highHandling.Id);
        var speederPos = state.RaceOrder.FindIndex(c => c.Id == lowHandling.Id);

        await Assert.That(handlerPos).IsLessThan(speederPos);
    }

    #endregion

    #region CheckIncident

    [Test]
    public async Task check_incident_returns_empty_when_no_incident() {
        // Use easy difficulty with day/sunny for lowest incident rate
        var race = CreateRace(cars: 4, difficulty: CircuitDifficulty.Easy);
        race.Start();

        // Run many times; at least one should return empty
        var emptyCount = 0;

        for (var i = 0; i < 100; i++) {
            var (_, events) = race.CheckIncident();
            if (events.Length == 0) emptyCount++;
        }

        await Assert.That(emptyCount).IsGreaterThan(0);
    }

    [Test]
    public async Task check_incident_dnf_removes_car_from_race_order() {
        var race = CreateRace(cars: 4);
        race.Start();

        // Force many incident checks until a DNF occurs
        var dnfFound = false;

        for (var i = 0; i < 1000 && !dnfFound; i++) {
            var (state, events) = race.CheckIncident();
            var incident = events.OfType<IncidentOccurred>().FirstOrDefault();

            if (incident is { Dnf: true }) {
                await Assert.That(state.RaceOrder.Any(c => c.Id == incident.CarId)).IsFalse();
                dnfFound = true;
            }
        }

        await Assert.That(dnfFound).IsTrue();
    }

    #endregion

    #region CheckWeather

    [Test]
    public async Task check_weather_toggles_between_sunny_and_rainy() {
        // Force weather change by running many checks on hard difficulty
        var race = CreateRace(cars: 2, difficulty: CircuitDifficulty.Hard);
        race.Start();

        WeatherChanged? change = null;

        for (var i = 0; i < 1000 && change is null; i++) {
            var (_, events) = race.CheckWeather();
            change = events.OfType<WeatherChanged>().FirstOrDefault();
        }

        await Assert.That(change).IsNotNull();
        await Assert.That(change!.OldWeather).IsNotEqualTo(change.NewWeather);
    }

    #endregion

    #region Finish

    [Test]
    public async Task finish_emits_single_race_finished_event() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var (_, events) = race.Finish();

        await Assert.That(events).HasCount().EqualTo(1);
        await Assert.That(events[0]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async Task finish_includes_all_cars_in_results() {
        var race = CreateRace(cars: 6);
        race.Start();
        race.SimulateLap();

        var (_, events)  = race.Finish();
        var finished     = (RaceFinished)events[0];

        await Assert.That(finished.Results).HasCount().EqualTo(6);
    }

    [Test]
    public async Task finish_positions_are_sequential() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var (_, events)  = race.Finish();
        var finished     = (RaceFinished)events[0];
        var positions    = finished.Results.Select(r => r.Position).OrderBy(p => p).ToList();

        await Assert.That(positions).IsEquivalentTo([1, 2, 3, 4]);
    }

    [Test]
    public async Task finish_leader_has_zero_gap() {
        var race = CreateRace(cars: 4);
        race.Start();
        race.SimulateLap();

        var (_, events) = race.Finish();
        var finished    = (RaceFinished)events[0];
        var leader      = finished.Results.First(r => r.Position == 1);

        await Assert.That(leader.GapToLeader).IsEqualTo(0.0);
    }

    #endregion

    #region DifficultyProfile

    [Test]
    public async Task night_reduces_handling_effectiveness() {
        var day   = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Sunny));
        var night = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Night, Weather.Sunny));

        await Assert.That(night.HandlingEffectiveness).IsLessThan(day.HandlingEffectiveness);
    }

    [Test]
    public async Task rain_reduces_top_speed_effectiveness() {
        var sunny = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Sunny));
        var rainy = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Rainy));

        await Assert.That(rainy.TopSpeedEffectiveness).IsLessThan(sunny.TopSpeedEffectiveness);
    }

    [Test]
    public async Task hard_difficulty_has_higher_incident_rate_than_easy() {
        var conditions = new RaceConditions(TimeOfDay.Day, Weather.Sunny);
        var easy       = DifficultyProfile.For(CircuitDifficulty.Easy, conditions);
        var hard       = DifficultyProfile.For(CircuitDifficulty.Hard, conditions);

        await Assert.That(hard.IncidentRate).IsGreaterThan(easy.IncidentRate);
    }

    #endregion
}
