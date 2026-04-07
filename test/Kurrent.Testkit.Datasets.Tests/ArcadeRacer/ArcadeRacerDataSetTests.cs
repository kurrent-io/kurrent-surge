// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testkit.Datasets.ArcadeRacer;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.Tests.ArcadeRacer;

[Category("ArcadeRacer")]
public class ArcadeRacerDataSetTests {
    static readonly ArcadeRacerDataSet DS = ArcadeRacerDataSet.Instance;

    #region RaceCar

    [Test]
    public async Task car_has_non_empty_name() {
        var car = DS.RaceCar.Generate();
        await Assert.That(car.Name).IsNotEmpty();
    }

    [Test]
    public async Task car_has_pilot_in_abbreviated_format() {
        var car = DS.RaceCar.Generate();
        await Assert.That(car.Pilot).Matches(@"^[A-Z]\. .+$");
    }

    [Test]
    public async Task car_spec_values_are_in_range() {
        var car = DS.RaceCar.Generate();
        await Assert.That(car.Spec.Acceleration).IsGreaterThanOrEqualTo(1).And.IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.TopSpeed).IsGreaterThanOrEqualTo(1).And.IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.Handling).IsGreaterThanOrEqualTo(1).And.IsLessThanOrEqualTo(10);
        await Assert.That(car.Spec.Endurance).IsGreaterThanOrEqualTo(1).And.IsLessThanOrEqualTo(10);
    }

    [Test]
    public async Task car_has_valid_id() {
        var car = DS.RaceCar.Generate();
        await Assert.That(car.Id).IsNotNullOrWhiteSpace();
    }

    [Test]
    public async Task generate_multiple_cars() {
        var cars = DS.RaceCar.Generate(8);
        await Assert.That(cars).Count().IsEqualTo(8);
    }

    [Test]
    public async Task generated_cars_have_unique_ids() {
        var cars = DS.RaceCar.Generate(10);
        var ids  = cars.Select(c => c.Id).Distinct().ToList();
        await Assert.That(ids).Count().IsEqualTo(10);
    }

    #endregion

    #region Circuit

    [Test]
    public async Task circuit_has_non_empty_name_and_country() {
        var circuit = DS.Circuit.Generate();
        await Assert.That(circuit.Name).IsNotEmpty();
        await Assert.That(circuit.Country).IsNotEmpty();
    }

    [Test]
    public async Task circuit_has_valid_dimensions() {
        var circuit = DS.Circuit.Generate();
        await Assert.That(circuit.Corners).IsGreaterThanOrEqualTo(4);
        await Assert.That(circuit.Straights).IsGreaterThanOrEqualTo(3);
        await Assert.That(circuit.LengthKm).IsGreaterThan(0.0);
    }

    #endregion

    #region RaceConditions

    [Test]
    public async Task conditions_generates_valid_values() {
        var conditions = RaceConditionsFaker.Instance.Generate();
        await Assert.That(conditions.TimeOfDay).IsNotEqualTo(TimeOfDay.Unspecified);
        await Assert.That(conditions.Weather).IsNotEqualTo(Weather.Unspecified);
    }

    #endregion

    #region Static Instance

    [Test]
    public async Task static_instance_is_reusable() {
        var car1 = ArcadeRacerDataSet.Instance.RaceCar.Generate();
        var car2 = ArcadeRacerDataSet.Instance.RaceCar.Generate();
        await Assert.That(car1.Id).IsNotEqualTo(car2.Id);
    }

    [Test]
    public async Task faker_instance_generates_independently() {
        var car1 = RaceCarFaker.Instance.Generate();
        var car2 = RaceCarFaker.Instance.Generate();
        await Assert.That(car1.Id).IsNotEqualTo(car2.Id);
    }

    #endregion
}
