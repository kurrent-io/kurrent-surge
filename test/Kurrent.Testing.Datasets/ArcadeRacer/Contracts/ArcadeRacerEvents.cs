// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Testing.Datasets.ArcadeRacer.Model;

namespace Testing.Datasets.ArcadeRacer.Contracts;

/// <summary>Marker interface for all events produced by the arcade race simulation.</summary>
public interface IRaceEvent {
    /// <summary>Unique identifier for this event instance.</summary>
    Guid EventId { get; init; }

    /// <summary>Unix timestamp in milliseconds when the event occurred.</summary>
    long Timestamp { get; init; }
}

/// <summary>Emitted when the starting grid is formed with cars, circuit, and conditions.</summary>
public record GridFormed : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Cars placed on the starting grid.</summary>
    public required RaceCar[] Cars { get; init; }

    /// <summary>The circuit being raced on.</summary>
    public required Circuit Circuit { get; init; }

    /// <summary>Initial weather and time-of-day conditions.</summary>
    public required RaceConditions Conditions { get; init; }

    /// <summary>Total number of laps in the race. Zero means unlimited.</summary>
    public required int TotalLaps { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted once at the start of the race, establishing the race clock.</summary>
public record RaceStarted : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted for each car at the end of a lap with timing data.</summary>
public record LapCompleted : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Identifier of the car that completed the lap.</summary>
    public required Guid CarId { get; init; }

    /// <summary>Lap number just completed (1-based).</summary>
    public required int Lap { get; init; }

    /// <summary>Time in seconds for this lap alone.</summary>
    public required double LapTime { get; init; }

    /// <summary>Total elapsed race time in seconds.</summary>
    public required double CumulativeTime { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted when a car gains a position by passing another car during a lap.</summary>
public record OvertakePerformed : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Identifier of the car that gained the position.</summary>
    public required Guid OvertakerId { get; init; }

    /// <summary>Identifier of the car that lost the position.</summary>
    public required Guid DefenderId { get; init; }

    /// <summary>Lap number when the overtake occurred.</summary>
    public required int Lap { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted when a car enters the pit lane for tire refresh, incurring a time penalty.</summary>
public record PitStopPerformed : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Identifier of the car that pitted.</summary>
    public required Guid CarId { get; init; }

    /// <summary>Lap number when the pit stop occurred.</summary>
    public required int Lap { get; init; }

    /// <summary>Time penalty in seconds added to cumulative time.</summary>
    public required double TimePenalty { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted when a car is involved in an incident that results in a time penalty.</summary>
public record IncidentOccurred : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Identifier of the affected car.</summary>
    public required Guid CarId { get; init; }

    /// <summary>Lap number when the incident occurred.</summary>
    public required int Lap { get; init; }

    /// <summary>Human-readable description of the incident (e.g. "tire puncture").</summary>
    public required string Description { get; init; }

    /// <summary>Time penalty in seconds added to cumulative time.</summary>
    public required double TimePenalty { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted when a car retires from the race (Did Not Finish).</summary>
public record CarRetired : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Identifier of the retired car.</summary>
    public required Guid CarId { get; init; }

    /// <summary>Lap number when the car retired.</summary>
    public required int Lap { get; init; }

    /// <summary>Human-readable description of the retirement cause.</summary>
    public required string Description { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted when weather conditions change mid-race, affecting the difficulty profile.</summary>
public record WeatherChanged : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Weather condition before the change.</summary>
    public required Weather OldWeather { get; init; }

    /// <summary>Weather condition after the change.</summary>
    public required Weather NewWeather { get; init; }

    /// <summary>Lap number when the change occurred.</summary>
    public required int Lap { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}

/// <summary>Emitted once at the end of the race with final standings for all cars.</summary>
public record RaceFinished : IRaceEvent {
    /// <inheritdoc />
    public required Guid EventId { get; init; }

    /// <summary>Ordered array of race results (finishers first, then DNFs).</summary>
    public required RaceResult[] Results { get; init; }

    /// <summary>Total laps completed in the race.</summary>
    public required int TotalLaps { get; init; }

    /// <inheritdoc />
    public required long Timestamp { get; init; }
}
