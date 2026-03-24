// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;

namespace Kurrent.Testkit.Datasets.ArcadeRacer;

/// <summary>
/// Entry point for generating arcade racer test data. Provides access to model fakers,
/// event fakers, and the race simulation builder.
/// </summary>
public class ArcadeRacerDataSet {
    public static readonly ArcadeRacerDataSet Instance = new();

    readonly Faker _faker;

    public ArcadeRacerDataSet(Faker? faker = null) {
        _faker = faker ?? new Faker();
        RaceCar = new RaceCarFaker(_faker);
        Circuit = new CircuitFaker(_faker);
        Events  = new EventFakers(_faker);
    }

    public RaceCarFaker RaceCar { get; }
    public CircuitFaker Circuit { get; }
    public EventFakers  Events  { get; }

    /// <summary>Creates a new race simulation builder using this dataset's faker.</summary>
    public ArcadeRaceBuilder NewRace() => new(_faker);

    public class EventFakers(Faker faker) {
        public GridFormedFaker        GridFormed        { get; } = new(faker);
        public RaceStartedFaker       RaceStarted       { get; } = new(faker);
        public LapCompletedFaker      LapCompleted      { get; } = new(faker);
        public OvertakePerformedFaker OvertakePerformed { get; } = new(faker);
        public PitStopPerformedFaker  PitStopPerformed  { get; } = new(faker);
        public IncidentOccurredFaker  IncidentOccurred  { get; } = new(faker);
        public CarRetiredFaker        CarRetired        { get; } = new(faker);
        public WeatherChangedFaker    WeatherChanged    { get; } = new(faker);
        public RaceFinishedFaker      RaceFinished      { get; } = new(faker);
    }
}
