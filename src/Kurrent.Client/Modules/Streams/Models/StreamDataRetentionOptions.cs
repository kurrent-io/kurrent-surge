#pragma warning disable CS8604

namespace Kurrent.Client.Streams;

[PublicAPI]
public record StreamDataRetentionOptions {
    public static readonly StreamDataRetentionOptions NoRetention = new();

    public int?      MaxCount { get; init; }
    public TimeSpan? MaxAge   { get; init; }
}
