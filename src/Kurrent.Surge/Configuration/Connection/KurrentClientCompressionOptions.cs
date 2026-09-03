using Kurrent.Surge.Validation;

namespace Kurrent.Surge.Client;

/// <summary>
/// Configuration for compressing outgoing gRPC calls.
/// </summary>
/// <remarks>
/// <para>
/// Disabled by default. Compression trades CPU for bandwidth, which is not universally a good
/// deal — small messages rarely benefit and already-compressed payloads never do.
/// </para>
/// <para>
/// When enabled, every call made through the client requests the configured <see cref="Algorithm"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class KurrentClientCompressionOptions : SelfValidatingClass<KurrentClientCompressionOptions, KurrentClientCompressionOptions.Validator> {
    /// <summary>
    /// Whether outgoing calls request compression.
    /// </summary>
    /// <remarks>
    /// Default value is <see langword="false"/>.
    /// </remarks>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The compression algorithm requested for outgoing calls when <see cref="Enabled"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default value is <see cref="CompressionAlgorithm.Gzip"/>.
    /// </para>
    /// <para>
    /// The KurrentDB server currently only implements <see cref="CompressionAlgorithm.Gzip"/> —
    /// see the remarks on <see cref="CompressionAlgorithm"/> before choosing another value.
    /// </para>
    /// </remarks>
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Gzip;

    /// <summary>
    /// The default compression configuration: disabled.
    /// </summary>
    public static KurrentClientCompressionOptions Default => new();

    public class Validator : Validator<Validator, KurrentClientCompressionOptions> {
        public Validator() {
            RuleFor(options => options.Algorithm)
                .Must(algorithm => Enum.IsDefined(algorithm), "Algorithm must be a defined CompressionAlgorithm value.");
        }
    }
}
