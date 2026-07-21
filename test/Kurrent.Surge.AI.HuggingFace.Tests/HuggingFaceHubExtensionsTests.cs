using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Live integration tests for the <see cref="HuggingFaceHubExtensions"/> static one-liners against the
/// <b>real</b> Hugging Face Hub. Network required; <c>[Category("Integration")]</c>. The one-liners take no
/// cache directory, so <c>HF_HUB_CACHE</c> — the documented override <see cref="HuggingFaceHub"/>
/// resolves against — redirects the cache root to a disposable temp directory for the duration of each test.
/// </summary>
[Timeout(120_000)]
[NotInParallel("hf-hub-cache-env")]
public sealed class HuggingFaceHubExtensionsTests {
    const string ModelUrl = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx";

    [Test]
    [Category("Integration")]
    public async ValueTask inspect_async_returns_a_planned_bundle(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        var previousCache  = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        try {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", cacheDirectory);

            // Act
            var bundle = await HuggingFaceHub.InspectAsync(ModelUrl, ct);

            // Assert — bundle planned and sized, primary file first.
            await Assert.That(bundle.Files).IsNotEmpty();
            await Assert.That(bundle.Files[0].RepoPath).IsEqualTo("onnx/model_quantized.onnx");
            await Assert.That(bundle.TotalBytes).IsGreaterThan(0L);
        } finally {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", previousCache);
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    [Category("Integration")]
    public async ValueTask download_async_downloads_every_file_to_disk(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        var previousCache  = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        var tracker        = new LoggerProgressTracker(NullLogger.Instance);
        try {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", cacheDirectory);

            // Act
            var bundle = await HuggingFaceHub.DownloadAsync(ModelUrl, tracker, ct: ct);

            // Assert — every planned file materialized on disk at its expected size.
            await Assert.That(bundle.Files).IsNotEmpty();
            foreach (var file in bundle.Files) {
                await Assert.That(File.Exists(file.LocalPath)).IsTrue();
                await Assert.That(new FileInfo(file.LocalPath).Length).IsEqualTo(file.Size);
            }
        } finally {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", previousCache);
            TestTemp.Cleanup(cacheDirectory);
        }
    }
}
