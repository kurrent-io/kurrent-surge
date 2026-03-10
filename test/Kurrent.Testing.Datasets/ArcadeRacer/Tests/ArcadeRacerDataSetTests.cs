// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Testing.Datasets.ArcadeRacer.Model;

namespace Testing.Datasets.ArcadeRacer.Tests;

[Category("ArcadeRacer")]
public class ArcadeRacerDataSetTests {
    [Test]
    public async Task car_has_non_empty_name() {
        var car = new Faker().ArcadeRacer().RaceCar();
        await Assert.That(car.Name).IsNotEmpty();
    }

    [Test]
    public async Task car_has_pilot_in_abbreviated_format() {
        var car = new Faker().ArcadeRacer().RaceCar();
        await Assert.That(car.Pilot).Matches(@"^[A-Z]\. .+$");
    }

    [Test]
    public async Task car_spec_values_are_in_range() {
        var car = new Faker().ArcadeRacer().RaceCar();
        await Assert.That(car.Spec.Acceleration).IsGreaterThanOrEqualTo(1);
        await Assert.That(car.Spec.Acceleration).IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.TopSpeed).IsGreaterThanOrEqualTo(1);
        await Assert.That(car.Spec.TopSpeed).IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.Handling).IsGreaterThanOrEqualTo(1);
        await Assert.That(car.Spec.Handling).IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.Endurance).IsGreaterThanOrEqualTo(1);
        await Assert.That(car.Spec.Endurance).IsLessThanOrEqualTo(10);
    }

    [Test]
    public async Task car_has_valid_id() {
        var car = new Faker().ArcadeRacer().RaceCar();
        await Assert.That(car.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task car_number_matches_parameter() {
        var car = new Faker().ArcadeRacer().RaceCar(number: 7);
        await Assert.That(car.Number).IsEqualTo(7);
    }

    [Test]
    public async Task grid_generates_requested_count() {
        var grid = new Faker().ArcadeRacer().Grid(8);
        await Assert.That(grid).HasCount().EqualTo(8);
    }

    [Test]
    public async Task grid_assigns_sequential_numbers() {
        var grid    = new Faker().ArcadeRacer().Grid(5);
        var numbers = grid.Select(c => c.Number).ToList();
        await Assert.That(numbers).IsEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Test]
    public async Task grid_cars_have_unique_ids() {
        var grid = new Faker().ArcadeRacer().Grid(10);
        var ids  = grid.Select(c => c.Id).Distinct().ToList();
        await Assert.That(ids).HasCount().EqualTo(10);
    }

    [Test]
    public async Task circuit_has_non_empty_name_and_country() {
        var circuit = new Faker().ArcadeRacer().Circuit();
        await Assert.That(circuit.Name).IsNotEmpty();
        await Assert.That(circuit.Country).IsNotEmpty();
    }

    [Test]
    public async Task circuit_has_valid_dimensions() {
        var circuit = new Faker().ArcadeRacer().Circuit();
        await Assert.That(circuit.Corners).IsGreaterThanOrEqualTo(4);
        await Assert.That(circuit.Straights).IsGreaterThanOrEqualTo(3);
        await Assert.That(circuit.LengthKm).IsGreaterThan(0.0);
    }

    [Test]
    public async Task circuit_uses_requested_difficulty() {
        var circuit = new Faker().ArcadeRacer().Circuit(CircuitDifficulty.Hard);
        await Assert.That(circuit.Difficulty).IsEqualTo(CircuitDifficulty.Hard);
    }

    [Test]
    public async Task conditions_generates_valid_values() {
        var conditions = new Faker().ArcadeRacer().Conditions();
        await Assert.That(conditions.TimeOfDay).IsNotEqualTo(TimeOfDay.Unspecified);
        await Assert.That(conditions.Weather).IsNotEqualTo(Weather.Unspecified);
    }

    [Test]
    public async Task faker_extension_returns_same_instance() {
        var faker = new Faker();
        var ds1 = faker.ArcadeRacer();
        var ds2 = faker.ArcadeRacer();
        await Assert.That(ds1).IsSameReferenceAs(ds2);
    }
}
