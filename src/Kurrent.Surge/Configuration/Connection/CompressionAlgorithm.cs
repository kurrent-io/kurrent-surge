namespace Kurrent.Surge.Client;

/// <summary>
/// Compression algorithm requested for outgoing gRPC calls.
/// </summary>
public enum CompressionAlgorithm {
    /// <summary>
    /// gzip compression. Broadly supported, moderate speed and ratio.
    /// </summary>
    Gzip = 0,

    /// <summary>
    /// Deflate compression. Similar to gzip without the container overhead.
    /// </summary>
    Deflate = 1,

    /// <summary>
    /// Brotli compression. Higher compression ratio at a higher CPU cost.
    /// </summary>
    Brotli = 2
}
