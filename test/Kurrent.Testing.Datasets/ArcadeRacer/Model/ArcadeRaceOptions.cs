namespace Testing.Datasets.ArcadeRacer.Model;

/// <summary>Configuration used to initialize an <see cref="ArcadeRace"/>.</summary>
record ArcadeRaceOptions {
    /// <summary>Cars on the starting grid.</summary>
    public List<RaceCar> Cars { get; init; } = [];

    /// <summary>Circuit to race on.</summary>
    public Circuit Circuit { get; init; } = new();

    /// <summary>Initial weather and time-of-day conditions.</summary>
    public RaceConditions Conditions { get; init; } = RaceConditions.Default;

    /// <summary>Number of laps. Zero means unlimited (streaming mode only).</summary>
    public int TotalLaps { get; init; }

    /// <summary>Delay between yielded events in streaming mode. Zero means no delay.</summary>
    public TimeSpan Pacing { get; init; }
}
