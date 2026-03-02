// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

#region Enums

public enum TimeOfDay { Unspecified, Day, Night }

public enum Weather { Unspecified, Sunny, Rainy }

public enum CircuitDifficulty { Easy, Medium, Hard }

public enum CarStatus { Racing, DidNotFinish }

#endregion

#region Value objects

public record struct CarSpec(int Acceleration, int TopSpeed, int Handling, int Endurance) {
    public static readonly CarSpec Default = new(5, 5, 5, 5);
}

public record struct RaceConditions(TimeOfDay TimeOfDay, Weather Weather) {
    public static readonly RaceConditions Unspecified = new(TimeOfDay.Unspecified, Weather.Unspecified);
    public static readonly RaceConditions Default     = new(TimeOfDay.Day, Weather.Sunny);
}

public record struct CarInfo(Guid CarId, string Name, int Number, string Pilot, CarSpec Spec);

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

public record DifficultyProfile {
    public double HandlingEffectiveness { get; init; } = 1.0;
    public double TopSpeedEffectiveness { get; init; } = 1.0;
    public double IncidentRate          { get; init; } = 0.05;
    public double DnfRate               { get; init; } = 0.30;
    public double WeatherChangeRate     { get; init; } = 0.03;
    public double LapTimeModifier       { get; init; } = 1.0;

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

#endregion

#region Entities

public class Car {
    public Guid      Id     { get; set; } = Guid.Empty;
    public string    Name   { get; set; } = "";
    public int       Number { get; set; }
    public string    Pilot  { get; set; } = "";
    public CarSpec   Spec   { get; set; } = CarSpec.Default;
    public CarStatus Status { get; set; } = CarStatus.Racing;

    public CarInfo ToInfo() => new(Id, Name, Number, Pilot, Spec);
}

public class Circuit {
    public Guid              Id         { get; set; } = Guid.Empty;
    public string            Name       { get; set; } = "";
    public string            Country    { get; set; } = "";
    public int               Corners    { get; set; }
    public int               Straights  { get; set; }
    public double            LengthKm   { get; set; }
    public CircuitDifficulty Difficulty { get; set; } = CircuitDifficulty.Medium;
}

#endregion

#region Event interface

public interface IRaceEvent {
    Guid EventId   { get; init; }
    long Timestamp { get; init; }
}

#endregion
