using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Starting position for reading or processing records. Can be either a <see cref="LogPosition"/> or <see cref="Revision"/>.
/// </summary>
public readonly partial record struct StartPosition : IVariant<LogPosition, Revision> {
    public static readonly StartPosition Unset = new(LogPosition.Unset);
}
