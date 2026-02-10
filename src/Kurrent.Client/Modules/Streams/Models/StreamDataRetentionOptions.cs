namespace Kurrent.Client.Streams;

#pragma warning disable CS8604
[PublicAPI]
public record StreamDataRetentionOptions {
    public static readonly StreamDataRetentionOptions NoRetention = new();

    public int?      MaxCount { get; init; }
    public TimeSpan? MaxAge   { get; init; }
}
