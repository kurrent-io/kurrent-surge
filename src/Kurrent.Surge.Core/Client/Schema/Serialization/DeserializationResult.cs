namespace Kurrent.Surge.Client.Schema.Serialization;

/// <summary>
/// The result of a deserialization attempt — a closed hierarchy so call sites pattern-match
/// the outcome instead of interpreting a bare <c>object?</c>, whose null conflated "empty
/// payload", "unknown schema", and "deserialized null". Failures (deserialization errors,
/// governance violations) remain exceptions.
/// </summary>
public abstract record DeserializationResult {
    // Closed hierarchy: only the nested cases below can derive.
    DeserializationResult() { }

    /// <summary>The value was materialized.</summary>
    public sealed record Deserialized(object Value) : DeserializationResult;

    /// <summary>
    /// The wire schema name has no mapped type in the client's catalogs; the data stays
    /// raw. Map the type in a message catalog or handle the raw payload.
    /// </summary>
    public sealed record UnknownType : DeserializationResult {
        public static readonly UnknownType Instance = new();
    }

    /// <summary>The payload is empty — there is nothing to deserialize.</summary>
    public sealed record NoData : DeserializationResult {
        public static readonly NoData Instance = new();
    }
}
