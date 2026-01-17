namespace Kurrent.Surge;

public static class TimeSpanExtensions {
    static readonly Random Random = new();

    public static TimeSpan GetFuzzyInterval(this TimeSpan interval, double fuzziness = 0.2) {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be greater than zero.");

        if (fuzziness is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(fuzziness), "Fuzziness must be between 0 and 1.");

        var range = interval.TotalMilliseconds * fuzziness;
        var minInterval = interval.TotalMilliseconds - range;
        var maxInterval = interval.TotalMilliseconds + range;
        var fuzzyInterval = TimeSpan.FromMilliseconds(Random.NextDouble() * (maxInterval - minInterval) + minInterval);

        return fuzzyInterval;
    }

    public static TimeSpan GetLowerFuzzyInterval(this TimeSpan interval, double fuzziness = 0.2) {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be greater than zero.");

        if (fuzziness is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(fuzziness), "Fuzziness must be between 0 and 1.");

        var minInterval   = interval.TotalMilliseconds * (1 - fuzziness);
        var maxInterval   = interval.TotalMilliseconds;
        var fuzzyInterval = TimeSpan.FromMilliseconds(Random.NextDouble() * (maxInterval - minInterval) + minInterval);

        return fuzzyInterval;
    }
}