using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;

[PublicAPI]
[method: SetsRequiredMembers]
public readonly record struct HeartbeatOptions(bool Enable, int RecordsThreshold) {
    public static readonly HeartbeatOptions Disabled = new(false, 0);

    public required bool Enable           { get; init; } = Enable;
    public required int  RecordsThreshold { get; init; } = RecordsThreshold;

    public static HeartbeatOptions Threshold(int recordsThreshold) =>
        new(true, recordsThreshold);
}
