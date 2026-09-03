using Grpc.Core;
using Kurrent.Toolkit.Grpc.Compression;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Internal;

public class GrpcCompressionTests {
    static readonly Dictionary<CompressionAlgorithm, string> ExpectedEncodingNames = new() {
        [CompressionAlgorithm.Gzip]    = "gzip",
        [CompressionAlgorithm.Deflate] = "deflate",
        [CompressionAlgorithm.Brotli]  = "br"
    };

    [Test]
    public async ValueTask maps_every_algorithm_to_its_registered_provider_encoding_name(CancellationToken cancellationToken) {
        // Arrange
        var allAlgorithms = Enum.GetValues<CompressionAlgorithm>();

        // Assert: the table is exhaustive — a new algorithm must be classified above first.
        await Assert.That(ExpectedEncodingNames.Keys.Order().SequenceEqual(allAlgorithms.Order())).IsTrue();

        // Act + Assert: the mapping used to build the wire header agrees with the table,
        // and — this is what regressed for Brotli — with the key each provider is actually
        // registered under in FastestCompressionProviders/OptimalCompressionProviders/etc.
        foreach (var algorithm in allAlgorithms) {
            var encodingName = algorithm.ToEncodingName();

            await Assert.That(encodingName).IsEqualTo(ExpectedEncodingNames[algorithm]);
            await Assert.That(GrpcCompression.OptimalCompressionProviders.ContainsKey(encodingName)).IsTrue();
        }
    }

    [Test]
    public async ValueTask with_compression_adds_the_header_when_absent(CancellationToken cancellationToken) {
        // Arrange
        var headers = new Metadata();

        // Act
        headers.WithCompression(CompressionAlgorithm.Brotli);

        // Assert
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("br");
    }

    [Test]
    public async ValueTask with_compression_does_not_duplicate_an_existing_header(CancellationToken cancellationToken) {
        // Arrange
        var headers = new Metadata { { GrpcCompression.CompressionRequestAlgorithmHeader, "gzip" } };

        // Act
        headers.WithCompression(CompressionAlgorithm.Brotli);

        // Assert: the caller's own choice is left alone.
        await Assert.That(headers.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("gzip");
    }

    [Test]
    public async ValueTask without_compression_requests_identity_encoding(CancellationToken cancellationToken) {
        // Arrange
        var headers = new Metadata();

        // Act
        headers.WithoutCompression();

        // Assert
        await Assert.That(headers.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("identity");
    }

    [Test]
    public async ValueTask without_compression_replaces_a_previously_requested_algorithm(CancellationToken cancellationToken) {
        // Arrange
        var headers = new Metadata();
        headers.WithCompression(CompressionAlgorithm.Gzip);

        // Act
        headers.WithoutCompression();

        // Assert: exactly one header, now requesting identity — not two conflicting entries.
        await Assert.That(headers.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("identity");
    }

    [Test]
    public async ValueTask without_compression_survives_a_later_blanket_compression_request(CancellationToken cancellationToken) {
        // Arrange: an opt-out, followed by whatever a blanket CompressionInterceptor would do
        // downstream — it must not clobber the opt-out, since "identity" already counts as
        // an existing compression header.
        var headers = new Metadata();
        headers.WithoutCompression();

        // Act
        headers.WithCompression(CompressionAlgorithm.Gzip);

        // Assert
        await Assert.That(headers.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("identity");
    }
}
