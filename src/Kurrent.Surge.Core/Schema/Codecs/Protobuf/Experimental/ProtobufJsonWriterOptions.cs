namespace Kurrent.Surge.Codecs.Protobuf.Experimental;

/// <summary>
/// Controls what a protobuf-to-JSON writer emits.
/// </summary>
[PublicAPI]
public sealed class ProtobufJsonWriterOptions {
    /// <summary>
    /// Emits the declared field name instead of its JSON name.
    /// </summary>
    /// <remarks>
    /// Off produces the lower camel case name the canonical mapping calls for. On produces
    /// <c>order_id</c> rather than <c>orderId</c>, which readers that key off the schema expect.
    /// </remarks>
    public bool UseProtoFieldNames { get; init; }

    /// <summary>
    /// Emits fields that are absent from the payload, carrying their default value.
    /// </summary>
    /// <remarks>
    /// Off follows the canonical mapping, which omits them. A field with implicit presence set to
    /// its default is indistinguishable from an absent one on the wire, so it is omitted either way
    /// unless this is on.
    /// </remarks>
    public bool EmitDefaults { get; init; }

    /// <summary>
    /// Emits enum values as their numbers instead of their names.
    /// </summary>
    /// <remarks>A value absent from the schema is emitted as a number regardless.</remarks>
    public bool EnumsAsIntegers { get; init; }

    /// <summary>
    /// How deeply nested a message may be before the payload is rejected.
    /// </summary>
    /// <remarks>
    /// Nesting is decoded by recursion, so an unbounded depth is a stack overflow waiting for the
    /// right payload — and a stack overflow cannot be caught. The default matches the limit the
    /// reference implementations enforce.
    /// </remarks>
    public int MaxDepth { get; init; } = DefaultMaxDepth;

    /// <summary>The nesting limit the reference protobuf implementations enforce.</summary>
    public const int DefaultMaxDepth = 100;

    internal static ProtobufJsonWriterOptions Default { get; } = new();
}
