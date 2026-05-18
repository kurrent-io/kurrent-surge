// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

/// <summary>Marker interface for all events produced by the arcade race simulation.</summary>
public interface IRaceEvent {
    /// <summary>Unique identifier for this event instance.</summary>
    Guid EventId { get; set; }

    /// <summary>Unix timestamp in milliseconds when the event occurred.</summary>
    long Timestamp { get; set; }

    /// <summary>
    /// Unique identifier for the race this event is associated with.
    /// </summary>
    string RaceId { get; set; }
}

/// <summary>Emitted when the starting grid is formed with cars, circuit, and conditions.</summary>
public class GridFormed : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Cars placed on the starting grid.</summary>
    public RaceCar[] Cars { get; set; } = [];

    /// <summary>The circuit being raced on.</summary>
    public Circuit Circuit { get; set; } = new();

    /// <summary>Initial weather and time-of-day conditions.</summary>
    public RaceConditions Conditions { get; set; } = new();

    /// <summary>Total number of laps in the race. Zero means unlimited.</summary>
    public int TotalLaps { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted once at the start of the race, establishing the race clock.</summary>
public class RaceStarted : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted for each car at the end of a lap with timing data.</summary>
public class LapCompleted : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Identifier of the car that completed the lap.</summary>
    public string CarId { get; set; }

    /// <summary>Lap number just completed (1-based).</summary>
    public int Lap { get; set; }

    /// <summary>Time in seconds for this lap alone.</summary>
    public double LapTime { get; set; }

    /// <summary>Total elapsed race time in seconds.</summary>
    public double CumulativeTime { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted when a car gains a position by passing another car during a lap.</summary>
public class OvertakePerformed : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Identifier of the car that gained the position.</summary>
    public string OvertakerId { get; set; }

    /// <summary>Identifier of the car that lost the position.</summary>
    public string DefenderId { get; set; }

    /// <summary>Lap number when the overtake occurred.</summary>
    public int Lap { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted when a car enters the pit lane for tire refresh, incurring a time penalty.</summary>
public class PitStopPerformed : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Identifier of the car that pitted.</summary>
    public string CarId { get; set; }

    /// <summary>Lap number when the pit stop occurred.</summary>
    public int Lap { get; set; }

    /// <summary>Time penalty in seconds added to cumulative time.</summary>
    public double TimePenalty { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted when a car is involved in an incident that results in a time penalty.</summary>
public class IncidentOccurred : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Identifier of the affected car.</summary>
    public string CarId { get; set; }

    /// <summary>Lap number when the incident occurred.</summary>
    public int Lap { get; set; }

    /// <summary>Human-readable description of the incident (e.g. "tire puncture").</summary>
    public string Description { get; set; } = "";

    /// <summary>Time penalty in seconds added to cumulative time.</summary>
    public double TimePenalty { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted when a car retires from the race (Did Not Finish).</summary>
public class CarRetired : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Identifier of the retired car.</summary>
    public string CarId { get; set; }

    /// <summary>Lap number when the car retired.</summary>
    public int Lap { get; set; }

    /// <summary>Human-readable description of the retirement cause.</summary>
    public string Description { get; set; } = "";

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted when weather conditions change mid-race, affecting the difficulty profile.</summary>
public class WeatherChanged : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Weather condition before the change.</summary>
    public Weather OldWeather { get; set; }

    /// <summary>Weather condition after the change.</summary>
    public Weather NewWeather { get; set; }

    /// <summary>Lap number when the change occurred.</summary>
    public int Lap { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>Emitted once at the end of the race with final standings for all cars.</summary>
public class RaceFinished : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Ordered array of race results (finishers first, then DNFs).</summary>
    public RaceResult[] Results { get; set; } = [];

    /// <summary>The race winner (first position in <see cref="Results"/>).</summary>
    public RaceResult? Winner => Results.Length > 0 ? Results[0] : null;

    /// <summary>Total laps completed in the race.</summary>
    public int TotalLaps { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}

/// <summary>
/// Emitted any time a significant insight is available about the race.
/// </summary>
public class RaceInsight : IRaceEvent {
    /// <inheritdoc />
    public Guid EventId { get; set; }

    /// <summary>Ordered array of race results (finishers first, then DNFs).</summary>
    public RaceResult[] Results { get; set; } = [];

    /// <summary>Total laps in the race.</summary>
    public int TotalLaps { get; set; }

    /// <inheritdoc />
    public long Timestamp { get; set; }

    /// <inheritdoc />
    public string RaceId { get; set; }
}
