using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Starting position for reading or processing records. Can be either a <see cref="LogPosition"/> or <see cref="StreamRevision"/>.
/// </summary>
public readonly partial record struct StartPosition : IVariant<LogPosition, StreamRevision> {
    public static readonly StartPosition Unset = new(LogPosition.Unset);
}
