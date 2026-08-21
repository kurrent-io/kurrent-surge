using System.Collections.Concurrent;
using Kurrent.Surge.Codecs.Avro.Binary;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Holds the generated encode/decode pair for each Avro message type.
/// </summary>
/// <remarks>
/// Generated code lives in the consumer's assembly, so it cannot be discovered by reference. Each
/// generated registration runs from a <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
/// in that assembly and registers itself here before any codec runs.
/// </remarks>
[PublicAPI]
public static class AvroCodecRegistry {
    static readonly ConcurrentDictionary<Type, AvroCodecEntry> Entries = new();

    public static void Register(Type messageType, Action<object, AvroBinaryEncoder> encode, Func<AvroBinaryDecoder, object> decode) =>
        Entries[messageType] = new AvroCodecEntry(encode, decode);

    public static bool TryGet(Type messageType, out AvroCodecEntry entry) => Entries.TryGetValue(messageType, out entry);

    public static bool IsRegistered(Type messageType) => Entries.ContainsKey(messageType);
}

/// <summary>
/// The generated encode/decode pair for one Avro message type.
/// </summary>
public readonly record struct AvroCodecEntry(
    Action<object, AvroBinaryEncoder> Encode,
    Func<AvroBinaryDecoder, object>   Decode);
