using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// The result of a read records operation, which can be either a <see cref="Record"/> or a <see cref="Heartbeat"/>.
/// </summary>
public readonly partial record struct ReadRecordsResult : IVariant<Record, Heartbeat> {
    internal static readonly ReadRecordsResult Irrelevant = new();
}
