// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

public interface IRaceEvent {
    Guid EventId   { get; init; }
    long Timestamp { get; init; }
}

public record RaceStarted : IRaceEvent {
    public required Guid           EventId    { get; init; }
    public required Circuit        Circuit    { get; init; }
    public required RaceConditions Conditions { get; init; }
    public required CarInfo[]      Cars       { get; init; }
    public required int            TotalLaps  { get; init; }
    public required long           Timestamp  { get; init; }
}

public record LapCompleted : IRaceEvent {
    public required Guid   EventId            { get; init; }
    public required Guid   CarId              { get; init; }
    public required string CarName            { get; init; }
    public required string Pilot              { get; init; }
    public required int    Lap                { get; init; }
    public required int    Position           { get; init; }
    public required double LapTime            { get; init; }
    public required double CumulativeTime     { get; init; }
    public required double GapToLeader        { get; init; }
    public required double EnduranceRemaining { get; init; }
    public required long   Timestamp          { get; init; }
}

public record OvertakePerformed : IRaceEvent {
    public required Guid   EventId     { get; init; }
    public required Guid   OvertakerId { get; init; }
    public required Guid   DefenderId  { get; init; }
    public required string Overtaker   { get; init; }
    public required string Defender    { get; init; }
    public required int    Lap         { get; init; }
    public required long   Timestamp   { get; init; }
}

public record PitStopPerformed : IRaceEvent {
    public required Guid   EventId         { get; init; }
    public required Guid   CarId           { get; init; }
    public required string Pilot           { get; init; }
    public required int    Lap             { get; init; }
    public required double TimePenalty     { get; init; }
    public required double EnduranceBefore { get; init; }
    public required double EnduranceAfter  { get; init; }
    public required long   Timestamp       { get; init; }
}

public record IncidentOccurred : IRaceEvent {
    public required Guid   EventId     { get; init; }
    public required Guid   CarId       { get; init; }
    public required string Pilot       { get; init; }
    public required int    Lap         { get; init; }
    public required string Description { get; init; }
    public required bool   Dnf         { get; init; }
    public required double TimePenalty { get; init; }
    public required long   Timestamp   { get; init; }
}

public record WeatherChanged : IRaceEvent {
    public required Guid    EventId    { get; init; }
    public required Weather OldWeather { get; init; }
    public required Weather NewWeather { get; init; }
    public required int     Lap        { get; init; }
    public required long    Timestamp  { get; init; }
}

public record RaceFinished : IRaceEvent {
    public required Guid         EventId   { get; init; }
    public required RaceResult[] Results   { get; init; }
    public required int          TotalLaps { get; init; }
    public required long         Timestamp { get; init; }
}
