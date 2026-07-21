using Bogus;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Pure unit tests for <see cref="HuggingFaceHub.ListCachedModels"/> against fabricated on-disk cache layouts —
/// no network, no real Hugging Face repo involved.
/// </summary>
public sealed class ListCachedModelsTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask returns_an_empty_list_when_the_cache_root_does_not_exist() {
        // Arrange
        var missingRoot = Path.Combine(TestTemp.NewDirectory(), "does-not-exist");
        var hub         = new HuggingFaceHub(new HubOptions { CacheDirectory = missingRoot });

        // Act
        var models = hub.ListCachedModels();

        // Assert
        await Assert.That(models).IsEmpty();
    }

    [Test]
    public async ValueTask returns_an_empty_list_for_an_empty_cache_root() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert
            await Assert.That(models).IsEmpty();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask lists_a_single_cached_snapshot_with_its_unmangled_repo_id() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var expectedFileCount  = Faker.Random.Int(1, 5);
            var expectedTotalBytes = CreateSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main", expectedFileCount, Faker);
            var expectedPath       = Path.Combine(cacheDirectory, "models--Xenova--all-MiniLM-L6-v2", "snapshots", "main");

            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert
            await Assert.That(models.Count).IsEqualTo(1);
            await Assert.That(models[0].Repo).IsEqualTo("Xenova/all-MiniLM-L6-v2");
            await Assert.That(models[0].Revision).IsEqualTo("main");
            await Assert.That(models[0].FileCount).IsEqualTo(expectedFileCount);
            await Assert.That(models[0].TotalBytes).IsEqualTo(expectedTotalBytes);
            await Assert.That(models[0].Path).IsEqualTo(expectedPath);
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask counts_files_in_nested_subdirectories() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var snapshotDir = Path.Combine(cacheDirectory, "models--Xenova--gte-small", "snapshots", "main");
            Directory.CreateDirectory(Path.Combine(snapshotDir, "onnx"));

            var rootFileSize   = WriteRandomFile(Path.Combine(snapshotDir, "config.json"), Faker);
            var nestedFileSize = WriteRandomFile(Path.Combine(snapshotDir, "onnx", "model.onnx"), Faker);

            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert
            await Assert.That(models.Count).IsEqualTo(1);
            await Assert.That(models[0].FileCount).IsEqualTo(2);
            await Assert.That(models[0].TotalBytes).IsEqualTo(rootFileSize + nestedFileSize);
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask lists_multiple_revisions_of_the_same_model_ordered_by_revision() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            CreateSnapshot(cacheDirectory, "acme", "model", "v2", 1, Faker);
            CreateSnapshot(cacheDirectory, "acme", "model", "v1", 1, Faker);

            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert — ordered by Repo then Revision (ordinal): v1 before v2.
            await Assert.That(models.Count).IsEqualTo(2);
            await Assert.That(models[0].Revision).IsEqualTo("v1");
            await Assert.That(models[1].Revision).IsEqualTo("v2");
            await Assert.That(models.All(m => m.Repo == "acme/model")).IsTrue();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask lists_multiple_models_ordered_by_repo() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            CreateSnapshot(cacheDirectory, "zeta", "model", "main", 1, Faker);
            CreateSnapshot(cacheDirectory, "alpha", "model", "main", 1, Faker);

            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert
            await Assert.That(models.Count).IsEqualTo(2);
            await Assert.That(models[0].Repo).IsEqualTo("alpha/model");
            await Assert.That(models[1].Repo).IsEqualTo("zeta/model");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask ignores_a_models_directory_with_no_snapshots_subdirectory() {
        // Arrange — a malformed / partially-written models--* directory with no snapshots/ at all.
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            Directory.CreateDirectory(Path.Combine(cacheDirectory, "models--broken--repo"));

            var hub = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });

            // Act
            var models = hub.ListCachedModels();

            // Assert
            await Assert.That(models).IsEmpty();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    // Creates {cacheRoot}/models--{owner}--{name}/snapshots/{revision}/ with `fileCount` random-content files,
    // returning the total bytes written.
    static long CreateSnapshot(string cacheRoot, string owner, string name, string revision, int fileCount, BogusFaker faker) {
        var snapshotDir = Path.Combine(cacheRoot, $"models--{owner}--{name}", "snapshots", revision);
        Directory.CreateDirectory(snapshotDir);

        long totalBytes = 0;

        for (var i = 0; i < fileCount; i++)
            totalBytes += WriteRandomFile(Path.Combine(snapshotDir, $"file{i}.bin"), faker);

        return totalBytes;
    }

    static long WriteRandomFile(string path, BogusFaker faker) {
        var bytes = faker.Random.Bytes(faker.Random.Int(16, 256));
        File.WriteAllBytes(path, bytes);
        return bytes.Length;
    }
}
