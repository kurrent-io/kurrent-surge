using Kurrent.Testkit.TUnit;
using TUnit.Assertions.Enums;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Verifies <see cref="GgufLayout"/> resolves the <c>.gguf</c> named by the <see cref="ModelRef"/>, expands a
/// split shard set from the named shard, and includes the optional <c>tokenizer.json</c>/<c>config.json</c>
/// companions — against REAL Hugging Face repo listings via <see cref="HuggingFaceClient.ListFilesAsync"/>.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public sealed class GgufLayoutTests {
    #region ->> Test Case Generators <<-

    public sealed class NamedQuantCases : TestCaseDataGenerator<(string Url, string ExpectedPrimary, string[] ExpectedFiles)> {
        protected override IEnumerable<TestCase<(string Url, string ExpectedPrimary, string[] ExpectedFiles)>> Data() {
            yield return new((
                "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/blob/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                ["tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf", "config.json"]
            ), DisplayName: "TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF");

            yield return new((
                "https://huggingface.co/TheBloke/Mistral-7B-Instruct-v0.2-GGUF/blob/main/mistral-7b-instruct-v0.2.Q5_K_M.gguf",
                "mistral-7b-instruct-v0.2.Q5_K_M.gguf",
                ["mistral-7b-instruct-v0.2.Q5_K_M.gguf", "config.json"]
            ), DisplayName: "TheBloke/Mistral-7B-Instruct-v0.2-GGUF");
        }
    }

    #endregion // Test Case Generators

    [Test]
    [NamedQuantCases]
    public async ValueTask resolves_the_named_quant_and_bundles_the_optional_config(string url, string expectedPrimary, string[] expectedFiles, CancellationToken ct) {
        // Arrange
        var client    = new HuggingFaceClient();
        var reference = new ModelRef(url);

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new GgufLayout().Select(files, reference);

        // Assert — tokenizer.json is absent in real GGUF conversions (the tokenizer is embedded in the file);
        // config.json is present and bundled as the best-effort companion.
        await Assert.That(plan.Primary.Path).IsEqualTo(expectedPrimary);
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(expectedFiles);
    }

    [Test]
    public async ValueTask returns_the_only_gguf_when_a_single_one_is_present(CancellationToken ct) {
        // Arrange — a "gguf-my-repo" single-quant conversion; nothing to disambiguate.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/ggml-org/gemma-1.1-2b-it-Q8_0-GGUF/blob/main/gemma-1.1-2b-it.Q8_0.gguf");

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new GgufLayout().Select(files, reference);

        // Assert
        await Assert.That(plan.Primary.Path).IsEqualTo("gemma-1.1-2b-it.Q8_0.gguf");
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(["gemma-1.1-2b-it.Q8_0.gguf"]);
    }

    [Test]
    public async ValueTask expands_a_split_shard_set_from_the_named_shard(CancellationToken ct) {
        // Arrange — a real 70B model split into 2 shards per quant; naming one shard resolves the whole set.
        var client = new HuggingFaceClient();
        const string firstShard = "Meta-Llama-3.1-70B-Instruct-Q6_K_L/Meta-Llama-3.1-70B-Instruct-Q6_K_L-00001-of-00002.gguf";
        var reference = new ModelRef($"https://huggingface.co/bartowski/Meta-Llama-3.1-70B-Instruct-GGUF/blob/main/{firstShard}");

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new GgufLayout().Select(files, reference);

        // Assert — primary is the named (first) shard; both shards are included, primary first.
        await Assert.That(plan.Primary.Path).IsEqualTo(firstShard);
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo([
            firstShard,
            "Meta-Llama-3.1-70B-Instruct-Q6_K_L/Meta-Llama-3.1-70B-Instruct-Q6_K_L-00002-of-00002.gguf"
        ], CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask throws_when_no_gguf_file_is_present(CancellationToken ct) {
        // Arrange — a real ONNX-only repo; the reference still names a .gguf file (none exist there).
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/model.gguf");
        var files     = await client.ListFilesAsync(reference.Url, ct);

        // Act & Assert
        var ex = Assert.Throws<ModelException>(() => new GgufLayout().Select(files, reference));
        await Assert.That(ex.Message).Contains(reference.Repo);
    }

    [Test]
    public async ValueTask throws_when_the_named_gguf_file_is_not_in_the_listing(CancellationToken ct) {
        // Arrange — a real GGUF repo, but the reference names a quant that doesn't exist there.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/blob/main/tinyllama-1.1b-chat-v1.0.Q7_K_M.gguf");
        var files     = await client.ListFilesAsync(reference.Url, ct);

        // Act & Assert
        var ex = Assert.Throws<ModelException>(() => new GgufLayout().Select(files, reference));
        await Assert.That(ex.Message).Contains(reference.Repo);
    }
}
