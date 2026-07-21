namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Live integration test for the standalone <see cref="HuggingFaceHub"/> entry point against the <b>real</b>
/// Hugging Face Hub — the full path: <see cref="HubOptions"/> ctor → token-bearing standalone factory →
/// list → plan (with companion relocation) → download a real bundle. Network required; <c>[Category("Integration")]</c>.
/// </summary>
[Timeout(120_000)]
public sealed class HuggingFaceHubTests {
    const string ModelUrl = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx";

    [Test]
    [Category("Integration")]
    public async ValueTask inspects_and_downloads_a_bundle_end_to_end(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act — inspect (list → auto-detect layout → plan) then download the whole bundle.
            var bundle = await hub.InspectModelAsync(new ModelRef(ModelUrl), ct);
            await bundle.DownloadAsync(ct: ct);

            // Assert — bundle planned and sized, every file materialized, and laid out in the HF-CLI snapshot dir.
            await Assert.That(bundle.Files).IsNotEmpty();
            await Assert.That(bundle.TotalBytes).IsGreaterThan(0L);
            await Assert.That(bundle.Files.All(f => File.Exists(f.LocalPath))).IsTrue();
            await Assert.That(bundle.Files[0].LocalPath).Contains("models--Xenova--all-MiniLM-L6-v2");
        } finally { TestTemp.Cleanup(cacheDirectory); }
    }

    [Test]
    [Category("Integration")]
    public async ValueTask second_download_reuses_the_cache(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });
            var bundle = await hub.InspectModelAsync(new ModelRef(ModelUrl), ct);

            await bundle.DownloadAsync(ct: ct);
            var writtenAt = File.GetLastWriteTimeUtc(bundle.Files[0].LocalPath);

            // Act — download again into the same cache; valid copies must be skipped, not re-fetched.
            await bundle.DownloadAsync(ct: ct);

            // Assert — the primary was not re-written.
            await Assert.That(File.GetLastWriteTimeUtc(bundle.Files[0].LocalPath)).IsEqualTo(writtenAt);
        } finally { TestTemp.Cleanup(cacheDirectory); }
    }
}
