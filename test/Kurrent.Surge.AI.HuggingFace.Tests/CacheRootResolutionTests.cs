namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Pure unit tests for <see cref="HuggingFaceHub"/>'s cache-root resolution, observed via
/// <see cref="HuggingFaceHub.CacheDirectory"/> — no network. Environment-touching tests run serially and
/// restore the environment; shares its <see cref="NotInParallelAttribute"/> key with
/// <see cref="HuggingFaceHubExtensionsTests"/>, which also reads <c>HF_HUB_CACHE</c>.
/// </summary>
[Timeout(10_000)]
[NotInParallel("hf-hub-cache-env")]
public sealed class CacheRootResolutionTests {
    [Test]
    public async ValueTask resolves_the_default_cache_under_the_user_profile() {
        // Arrange — no explicit CacheDirectory and no HF_HUB_CACHE: the true default path. Mirrors
        // HuggingFaceHub.ResolveCacheRoot's own fallback (UserProfile, else CurrentDirectory) rather than
        // assuming UserProfile always resolves.
        var previousEnv = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        var home         = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Directory.GetCurrentDirectory();
        var expected = Path.GetFullPath(Path.Combine(home, ".cache", "huggingface", "hub"));
        try {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", null);

            // Act
            var hub = new HuggingFaceHub(new HubOptions());

            // Assert
            await Assert.That(hub.CacheDirectory).IsEqualTo(expected);
        } finally {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", previousEnv);
        }
    }

    [Test]
    public async ValueTask hf_hub_cache_wins_over_the_default() {
        // Arrange
        var previousEnv    = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        var cacheDirectory = TestTemp.NewDirectory();
        var expected       = Path.GetFullPath(cacheDirectory);
        try {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", cacheDirectory);

            // Act
            var hub = new HuggingFaceHub(new HubOptions());

            // Assert
            await Assert.That(hub.CacheDirectory).IsEqualTo(expected);
        } finally {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", previousEnv);
            TestTemp.Cleanup(cacheDirectory);
        }
    }
}
