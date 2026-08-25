namespace Kurrent.Surge.Codecs.Protobuf.Experimental;

/// <summary>
/// Controls what a protobuf-to-JSON writer emits.
/// </summary>
/// <remarks>
/// There is one output form — the canonical JSON mapping — so nothing here changes what the JSON
/// looks like. A setting that did would have to be told to the schema exporter as well, or the
/// exported schema would reject the very payload this writer produced.
/// </remarks>
[PublicAPI]
public class ProtobufJsonWriterOptions {
    /// <summary>
    /// How deeply nested a message may be before the payload is rejected.
    /// </summary>
    /// <remarks>
    /// Nesting is decoded by recursion, so an unbounded depth is a stack overflow waiting for the
    /// right payload — and a stack overflow cannot be caught. A recursive message type lets a few
    /// bytes ask for thousands of levels, which makes this a limit on hostile input rather than a
    /// tuning knob. The default matches what the reference implementations enforce.
    /// </remarks>
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    /// <summary>The nesting limit the reference protobuf implementations enforce.</summary>
    public const int DefaultMaxDepth = 100;

    internal static ProtobufJsonWriterOptions Default { get; } = new();
}
