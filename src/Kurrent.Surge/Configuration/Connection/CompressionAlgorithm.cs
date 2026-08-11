namespace Kurrent.Surge.Client;

/// <summary>
/// Compression algorithm requested for outgoing gRPC calls.
/// </summary>
/// <remarks>
/// As of this writing, the KurrentDB server only implements <see cref="Gzip"/>. Requesting
/// <see cref="Deflate"/> or <see cref="Brotli"/> makes every call fail with an
/// <c>Unimplemented</c> status — the server rejects the encoding outright rather than
/// falling back to no compression. They're kept here for forward compatibility with a
/// future server version; verified with <c>CompressionTests</c>
/// (test/Kurrent.Surge.Tests/Integration/Client), which will start failing the moment the
/// server adds support and needs this note updated.
/// </remarks>
public enum CompressionAlgorithm {
    /// <summary>
    /// gzip compression. Broadly supported, moderate speed and ratio. The only algorithm
    /// the KurrentDB server currently accepts.
    /// </summary>
    Gzip = 0,

    /// <summary>
    /// Deflate compression. Similar to gzip without the container overhead.
    /// </summary>
    /// <remarks>
    /// Not yet supported by the KurrentDB server — see the enum-level remarks.
    /// </remarks>
    Deflate = 1,

    /// <summary>
    /// Brotli compression. Higher compression ratio at a higher CPU cost.
    /// </summary>
    /// <remarks>
    /// Not yet supported by the KurrentDB server — see the enum-level remarks.
    /// </remarks>
    Brotli = 2
}
