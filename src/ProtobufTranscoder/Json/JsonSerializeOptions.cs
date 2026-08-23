namespace ProtobufTranscoder.Json;

public sealed class JsonSerializeOptions
{
    /// <summary>Use proto field names (snake_case) instead of camelCase.</summary>
    public bool UseProtoFieldNames { get; init; }

    /// <summary>Emit fields even when they have default values.</summary>
    public bool EmitDefaults { get; init; }

    /// <summary>Emit enum values as integers instead of string names.</summary>
    public bool EnumsAsIntegers { get; init; }
}
