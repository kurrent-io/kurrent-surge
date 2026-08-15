namespace Kurrent.Surge.Tests.Options;

public class KurrentClientCompressionOptionsTests {
    [Test]
    public async ValueTask default_compression_options_disable_compression(CancellationToken cancellationToken) {
        // Act
        var options = KurrentClientCompressionOptions.Default;

        // Assert
        await Assert.That(options.Enabled).IsFalse();
        await Assert.That(options.Algorithm).IsEqualTo(CompressionAlgorithm.Gzip);
    }

    [Test]
    public async ValueTask creating_custom_compression_options_stores_expected_values(CancellationToken cancellationToken) {
        // Act
        var options = new KurrentClientCompressionOptions {
            Enabled   = true,
            Algorithm = CompressionAlgorithm.Brotli
        };

        // Assert
        await Assert.That(options.Enabled).IsTrue();
        await Assert.That(options.Algorithm).IsEqualTo(CompressionAlgorithm.Brotli);
    }
}
