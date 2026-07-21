using Kurrent.Testkit.TUnit;
using TUnit.Assertions.Enums;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Verifies <see cref="OnnxLayout"/> resolves the primary <c>.onnx</c> named by the <see cref="ModelRef"/> and
/// bundles its companions (external-data shards, tokenizer, config) from a REAL Hugging Face repo listing —
/// dogfooding <see cref="HuggingFaceClient.ListFilesAsync"/>, then running the layout's pure selection against it.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public sealed class OnnxLayoutTests {
    #region ->> Test Case Generators <<-

    public sealed class RepoCompanionCases : TestCaseDataGenerator<(string Url, string ExpectedPrimary, string[] ExpectedFiles)> {
        protected override IEnumerable<TestCase<(string Url, string ExpectedPrimary, string[] ExpectedFiles)>> Data() {
            yield return new((
                "https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model.onnx",
                "onnx/model.onnx",
                ["onnx/model.onnx", "tokenizer.json", "tokenizer_config.json", "vocab.txt", "special_tokens_map.json", "config.json"]
            ), DisplayName: "Xenova/all-MiniLM-L6-v2");

            yield return new((
                "https://huggingface.co/Xenova/gte-small/blob/main/onnx/model.onnx",
                "onnx/model.onnx",
                ["onnx/model.onnx", "tokenizer.json", "tokenizer_config.json", "vocab.txt", "special_tokens_map.json", "config.json"]
            ), DisplayName: "Xenova/gte-small");

            yield return new((
                "https://huggingface.co/Xenova/bge-small-en-v1.5/blob/main/onnx/model.onnx",
                "onnx/model.onnx",
                ["onnx/model.onnx", "tokenizer.json", "tokenizer_config.json", "vocab.txt", "special_tokens_map.json", "config.json"]
            ), DisplayName: "Xenova/bge-small-en-v1.5");
        }
    }

    #endregion // Test Case Generators

    [Test]
    [RepoCompanionCases]
    public async ValueTask resolves_the_named_onnx_and_bundles_its_companions(string url, string expectedPrimary, string[] expectedFiles, CancellationToken ct) {
        // Arrange
        var client    = new HuggingFaceClient();
        var reference = new ModelRef(url);

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new OnnxLayout().Select(files, reference);

        // Assert — exact set match: companions bundled, unrelated repo files (README.md, other quant variants) excluded.
        await Assert.That(plan.Primary.Path).IsEqualTo(expectedPrimary);
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo(expectedFiles);
    }

    [Test]
    public async ValueTask orders_external_data_before_named_artifacts(CancellationToken ct) {
        // Arrange — a real ONNX-LLM repo with external-data weights and 20+ variants; the URL names one exactly.
        var client = new HuggingFaceClient();
        const string primaryFile = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx";
        var reference = new ModelRef($"https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/blob/main/{primaryFile}");

        // Act
        var files = await client.ListFilesAsync(reference.Url, ct);
        var plan  = new OnnxLayout().Select(files, reference);

        // Assert — external-data shard first, then tokenizer artifacts, then config artifacts, in that order.
        await Assert.That(plan.Primary.Path).IsEqualTo(primaryFile);
        await Assert.That(plan.Files.Select(f => f.Path)).IsEquivalentTo([
            primaryFile,
            $"{primaryFile}.data",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/tokenizer.json",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/tokenizer.model",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/tokenizer_config.json",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/special_tokens_map.json",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/added_tokens.json",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/config.json",
            "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/genai_config.json"
        ], CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask throws_when_the_named_onnx_file_is_not_in_the_listing(CancellationToken ct) {
        // Arrange — a real GGUF-only repo; the reference names a .onnx file that doesn't exist there.
        var client    = new HuggingFaceClient();
        var reference = new ModelRef("https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/blob/main/model.onnx");
        var files     = await client.ListFilesAsync(reference.Url, ct);

        // Act & Assert
        var ex = Assert.Throws<ModelException>(() => new OnnxLayout().Select(files, reference));
        await Assert.That(ex.Message).Contains(reference.Repo);
    }
}
