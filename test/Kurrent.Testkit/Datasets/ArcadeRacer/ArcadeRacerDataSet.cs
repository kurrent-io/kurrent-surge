// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;

namespace Kurrent.Testkit.Datasets.ArcadeRacer;

/// <summary>
/// Entry point for generating arcade racer test data. Provides access to model fakers,
/// event fakers, and the race simulation builder.
/// </summary>
public class ArcadeRacerDataSet : CustomDataSet<ArcadeRacerDataSet> {
    public static readonly ArcadeRacerDataSet Instance = new();

    protected override void Initialize() { }

    public ArcadeRacerDataSet(Faker? faker = null) : base(faker ?? new Faker()) {
        RaceCar    = new RaceCarFaker(Faker);
        Circuit    = new CircuitFaker(Faker);
        RaceEvents = new EventFakers(Faker);
    }

    /// <summary>Creates a pre-computed race builder that shares this dataset's Faker for reproducible data.</summary>
    public PrecomputedRaceSimulatorBuilder Race => new(Faker);

    public RaceCarFaker  RaceCar    { get; }
    public CircuitFaker  Circuit    { get; }
    public EventFakers   RaceEvents { get; }

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

public static class FakerExtensions {
    extension(Faker faker) {
        public ArcadeRacerDataSet ArcadeRacer() =>
            ContextHelper.GetOrSet(faker, f => new ArcadeRacerDataSet(f));
    }
}
