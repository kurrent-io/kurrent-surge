using TUnit.Assertions.Enums;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Verifies <see cref="SafetensorsLayout"/> resolves the single weight file named by the <see cref="ModelRef"/>,
/// or a sharded model when a <c>*.safetensors.index.json</c> is present — every <c>.safetensors</c> shard plus
/// the index — together with config/tokenizer companions. Against REAL Hugging Face repo listings via
/// <see cref="HuggingFaceClient.ListFilesAsync"/>.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public sealed class SafetensorsLayoutTests {
    #region ->> Test Case Generators <<-

    public sealed class SingleWeightRepoCases : TestCaseDataGenerator<(string Url, string[] ExpectedFiles)> {
        protected override IEnumerable<TestCase<(string Url, string[] ExpectedFiles)>> Data() {
            // Both repos carry a nested 1_Pooling/config.json that sorts BEFORE the root config.json in the
            // HF tree listing — the companion picked must be the root one (beside the weights), not the
            // pooling-module sidecar, or the flattened bundle ships the wrong config.json contents.
            yield return new((
                "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/blob/main/model.safetensors",
                ["model.safetensors", "config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json"]
            ), DisplayName: "sentence-transformers/all-MiniLM-L6-v2");

            yield return new((
                "https://huggingface.co/BAAI/bge-small-en-v1.5/blob/main/model.safetensors",
                ["model.safetensors", "config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json"]
            ), DisplayName: "BAAI/bge-small-en-v1.5");
        }
    }

    public sealed class ShardedRepoCases : TestCaseDataGenerator<(string Url, string[] ExpectedFiles)> {
        protected override IEnumerable<TestCase<(string Url, string[] ExpectedFiles)>> Data() {
            yield return new((
                "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct/blob/main/model-00001-of-00002.safetensors",
                [
                    "model.safetensors.index.json", "model-00001-of-00002.safetensors", "model-00002-of-00002.safetensors",
                    "config.json", "tokenizer.json", "tokenizer.model", "tokenizer_config.json", "special_tokens_map.json"
                ]
            ), DisplayName: "microsoft/Phi-3-mini-4k-instruct (2 shards)");

            yield return new((
                "https://huggingface.co/Qwen/Qwen2.5-7B-Instruct/blob/main/model-00001-of-00004.safetensors",
                [
                    "model.safetensors.index.json",
                    "model-00001-of-00004.safetensors", "model-00002-of-00004.safetensors",
                    "model-00003-of-00004.safetensors", "model-00004-of-00004.safetensors",
                    "config.json", "tokenizer.json", "tokenizer_config.json", "vocab.json", "merges.txt"
                ]
            ), DisplayName: "Qwen/Qwen2.5-7B-Instruct (4 shards)");
        }
    }

    #endregion // Test Case Generators

    [Test]
    public async ValueTask selects_the_named_weight_file_and_bundles_its_companions(CancellationToken ct) {
        // Arrange — a flat repo: model.safetensors + config/tokenizer companions, no nested duplicates.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct/blob/main/model.safetensors");

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new SafetensorsLayout().Select(files, reference);

        // Assert — generation_config.json is real but not a wanted companion name, so it's excluded.
        await Assert.That(plan.Primary.Path).IsEqualTo("model.safetensors");
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo([
            "model.safetensors", "config.json", "tokenizer.json", "tokenizer_config.json", "vocab.json", "merges.txt"
        ]);
    }

    [Test]
    [SingleWeightRepoCases]
    public async ValueTask prefers_companions_from_the_primary_directory_over_nested_copies(string url, string[] expectedFiles, CancellationToken ct) {
        // Arrange
        var client    = new HuggingFaceClient();
        var reference = new ModelRef(url);

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new SafetensorsLayout().Select(files, reference);

        // Assert — root config.json bundled, NOT the nested 1_Pooling/config.json the listing yields first.
        await Assert.That(plan.Primary.Path).IsEqualTo("model.safetensors");
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(expectedFiles, CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask resolves_the_named_file_among_multiple_safetensors(CancellationToken ct) {
        // Arrange — a real repo shipping 3 unrelated .safetensors files (no "model.safetensors", no index).
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/madebyollin/sdxl-vae-fp16-fix/blob/main/sdxl_vae.safetensors");

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new SafetensorsLayout().Select(files, reference);

        // Assert
        await Assert.That(plan.Primary.Path).IsEqualTo("sdxl_vae.safetensors");
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(["sdxl_vae.safetensors", "config.json"]);
    }

    [Test]
    [ShardedRepoCases]
    public async ValueTask sharded_repo_uses_the_index_as_primary_and_includes_every_shard(string url, string[] expectedFiles, CancellationToken ct) {
        // Arrange — the URL names one shard; the index is the primary regardless, since it's auto-detected.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef(url);

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new SafetensorsLayout().Select(files, reference);

        // Assert — index first, then every shard (ordinal), then companions in priority order.
        await Assert.That(plan.Primary.Path).IsEqualTo(expectedFiles[0]);
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(expectedFiles, CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask throws_when_the_named_file_is_not_in_the_listing(CancellationToken ct) {
        // Arrange — a real GGUF-only repo; the reference names a .safetensors file that doesn't exist there.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/blob/main/model.safetensors");
        var files     = await client.ListFilesAsync(reference.Url, ct);

        // Act & Assert
        var ex = Assert.Throws<ModelException>(() => new SafetensorsLayout().Select(files, reference));
        await Assert.That(ex.Message).Contains(reference.Repo);
    }
}
