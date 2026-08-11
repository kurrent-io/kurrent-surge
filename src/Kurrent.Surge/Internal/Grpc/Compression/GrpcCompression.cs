using System.IO.Compression;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Compression;
using Kurrent.Surge.Client;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Grpc.Compression;

static class GrpcCompression {
    public const string CompressionRequestAlgorithmHeader = "grpc-internal-encoding-request";

    // The standard grpc-encoding value meaning "do not compress this message".
    const string IdentityEncoding = "identity";

    public static readonly Dictionary<string, ICompressionProvider> FastestCompressionProviders = new(StringComparer.OrdinalIgnoreCase) {
        ["gzip"]    = new GzipCompressionProvider(CompressionLevel.Fastest),
        ["deflate"] = new DeflateCompressionProvider(CompressionLevel.Fastest),
        ["br"]      = new BrotliCompressionProvider(CompressionLevel.Fastest),
    };

    public static readonly Dictionary<string, ICompressionProvider> OptimalCompressionProviders = new(StringComparer.OrdinalIgnoreCase) {
        ["gzip"]    = new GzipCompressionProvider(CompressionLevel.Optimal),
        ["deflate"] = new DeflateCompressionProvider(CompressionLevel.Optimal),
        ["br"]      = new BrotliCompressionProvider(CompressionLevel.Optimal),
    };

    public static readonly Dictionary<string, ICompressionProvider> SmallestSizeCompressionProviders = new(StringComparer.OrdinalIgnoreCase) {
        ["gzip"]    = new GzipCompressionProvider(CompressionLevel.SmallestSize),
        ["deflate"] = new DeflateCompressionProvider(CompressionLevel.SmallestSize),
        ["br"]      = new BrotliCompressionProvider(CompressionLevel.SmallestSize),
    };

    /// <summary>
    /// Provides a collection of default gRPC compression providers, which includes
    /// providers for "gzip", "deflate", and "br" (Brotli) compression methods.
    /// </summary>
    /// <remarks>
    /// The default compression providers are initialized with the fastest compression level
    /// for optimal performance. This collection is used to enable support for these
    /// compression algorithms in gRPC communication.
    /// </remarks>
    public static readonly IList<ICompressionProvider> DefaultFastestProviders = FastestCompressionProviders.Values.ToList();

    /// <summary>
    /// Provides a collection of gRPC compression providers configured with the "Optimal" compression level,
    /// including support for "gzip", "deflate", and "br" (Brotli) compression methods.
    /// </summary>
    /// <remarks>
    /// This collection is designed for scenarios requiring a balance between compression efficiency and speed,
    /// using the "Optimal" compression level for each supported algorithm in gRPC communication.
    /// </remarks>
    public static readonly IList<ICompressionProvider> DefaultOptimalProviders = OptimalCompressionProviders.Values.ToList();

    /// <summary>
    /// Provides a collection of default gRPC compression providers configured to use
    /// the smallest size compression level. This collection includes providers for
    /// "gzip", "deflate", and "br" (Brotli) compression methods.
    /// </summary>
    /// <remarks>
    /// The smallest size compression level prioritizes achieving the smallest possible
    /// data size, often at the cost of increased processing time. This collection is
    /// typically used when minimizing data size is a higher priority than compression
    /// throughput.
    /// </remarks>
    public static readonly IList<ICompressionProvider> DefaultSmallestSizeProviders = SmallestSizeCompressionProviders.Values.ToList();

    /// <summary>
    /// Configures the specified gRPC channel options with a set of compression providers corresponding to the specified compression level.
    /// </summary>
    /// <param name="options">The gRPC channel options to which the compression providers will be added.</param>
    /// <param name="level">The desired compression level. Supported levels are Fastest, Optimal, and SmallestSize. Defaults to Optimal.</param>
    /// <returns>The updated <see cref="GrpcChannelOptions"/> with the appropriate compression providers applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the specified compression level is NoCompression or if an unsupported compression level is provided.</exception>
    public static GrpcChannelOptions WithCompressionProviders(this GrpcChannelOptions options, CompressionLevel level = CompressionLevel.Optimal) {
        if (level == CompressionLevel.NoCompression)
            throw new ArgumentOutOfRangeException(nameof(level), "Compression level cannot be NoCompression");

        options.CompressionProviders = level switch {
            CompressionLevel.Fastest      => DefaultFastestProviders,
            CompressionLevel.Optimal      => DefaultOptimalProviders,
            CompressionLevel.SmallestSize => DefaultSmallestSizeProviders,
            _                             => throw new ArgumentOutOfRangeException(nameof(level), "Unsupported compression level")
        };

        return options;
    }

    /// <summary>
    /// Adds the specified compression algorithm to the gRPC call options. If a compression header
    /// with the key "grpc-internal-encoding-request" is not already present, it appends it with
    /// the selected algorithm.
    /// </summary>
    /// <param name="options">The existing gRPC call options.</param>
    /// <param name="algorithm">The compression algorithm to apply. Defaults to Gzip if not specified.</param>
    /// <returns>The updated CallOptions with the compression header added if it was not already present.</returns>
    public static CallOptions WithCompression(this CallOptions options, CompressionAlgorithm algorithm = CompressionAlgorithm.Gzip) {
        options.Headers.WithCompression(algorithm);
        return options;
    }

    /// <summary>
    /// Removes any existing compression settings from the gRPC call options by requesting the
    /// "identity" (no compression) encoding explicitly.
    /// </summary>
    /// <param name="options">The existing gRPC call options to be modified.</param>
    /// <returns>The updated CallOptions with compression disabled.</returns>
    public static CallOptions WithoutCompression(this CallOptions options) {
        options.Headers.WithoutCompression();
        return options;
    }

    /// <summary>
    /// Adds the specified compression algorithm to the gRPC call headers. If a compression header
    /// with the key "grpc-internal-encoding-request" is not already present, it appends it with
    /// the selected algorithm.
    /// </summary>
    /// <param name="headers">The existing headers to which the compression header will be added.</param>
    /// <param name="algorithm">The compression algorithm to apply. Defaults to Gzip if not specified.</param>
    /// <returns>The updated headers with the compression header added if it was not already present.</returns>
    public static Metadata? WithCompression(this Metadata? headers, CompressionAlgorithm algorithm = CompressionAlgorithm.Gzip) {
        headers?.TryAddIfAbsent(new Metadata.Entry(CompressionRequestAlgorithmHeader, algorithm.ToEncodingName()));
        return headers;
    }

    /// <summary>
    /// Requests the "identity" (no compression) encoding, overriding any compression algorithm
    /// previously set on the headers — including one a blanket compression interceptor would
    /// otherwise apply, since "identity" counts as an already-present compression header.
    /// </summary>
    /// <param name="headers">The existing metadata headers to modify. Can be null.</param>
    /// <returns>The modified metadata requesting no compression, or the original metadata if the input was null.</returns>
    public static Metadata? WithoutCompression(this Metadata? headers) {
        if (headers is null)
            return headers;

        headers.RemoveCompressionHeader();
        headers.Add(CompressionRequestAlgorithmHeader, IdentityEncoding);

        return headers;
    }

    /// <summary>
    /// Maps a <see cref="CompressionAlgorithm"/> to its grpc-encoding wire name.
    /// </summary>
    public static string ToEncodingName(this CompressionAlgorithm algorithm) =>
        algorithm switch {
            CompressionAlgorithm.Gzip    => "gzip",
            CompressionAlgorithm.Deflate => "deflate",
            CompressionAlgorithm.Brotli  => "br",
            _                            => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported compression algorithm.")
        };

    /// <summary>
    /// Adds <paramref name="entry"/> unless a header with the same key (case-insensitive) is
    /// already present. Iterates by index to avoid the enumerator allocation LINQ's Any/FirstOrDefault
    /// would incur going through Metadata's IEnumerable interface.
    /// </summary>
    /// <returns><see langword="true"/> if the entry was added.</returns>
    internal static bool TryAddIfAbsent(this Metadata headers, Metadata.Entry entry) {
        for (var i = 0; i < headers.Count; i++) {
            if (string.Equals(headers[i].Key, entry.Key, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        headers.Add(entry);
        return true;
    }

    internal static void RemoveCompressionHeader(this Metadata headers) {
        for (var i = 0; i < headers.Count; i++) {
            if (string.Equals(headers[i].Key, CompressionRequestAlgorithmHeader, StringComparison.OrdinalIgnoreCase)) {
                headers.RemoveAt(i);
                return;
            }
        }
    }
}
