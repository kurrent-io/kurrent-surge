using Kurrent.Testkit.TUnit;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Verifies <see cref="ModelRef"/> derives endpoint / repo / revision / file path / kind purely from a direct
/// Hugging Face file URL (<c>…/blob|resolve/{rev}/{path}</c>), and rejects anything that isn't one —
/// repository (<c>…/tree/…</c>) URLs, a bare repo id, a non-absolute string, and a file that isn't a known
/// model format. Pure parsing: no network, fully deterministic.
/// </summary>
[Timeout(10_000)]
public sealed class ModelRefTests {
    #region ->> Test Case Generators <<-

    public sealed class ModelFileUrlCases
        : TestCaseDataGenerator<(string Url, string ExpectedEndpoint, string ExpectedRepo, string ExpectedRevision, string ExpectedFilePath, ModelKind ExpectedKind)> {
        protected override IEnumerable<TestCase<(string Url, string ExpectedEndpoint, string ExpectedRepo, string ExpectedRevision, string ExpectedFilePath, ModelKind ExpectedKind)>> Data() {
            yield return new((
                "https://huggingface.co/Xenova/multilingual-e5-small/blob/main/onnx/model_quantized.onnx",
                "https://huggingface.co", "Xenova/multilingual-e5-small", "main", "onnx/model_quantized.onnx", ModelKind.Onnx
            ), DisplayName: "blob url yields full file path");

            yield return new((
                "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx",
                "https://huggingface.co", "Xenova/multilingual-e5-small", "main", "onnx/model_quantized.onnx", ModelKind.Onnx
            ), DisplayName: "resolve url yields full file path");

            yield return new((
                "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/blob/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf",
                "https://huggingface.co", "TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF", "main", "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf", ModelKind.Gguf
            ), DisplayName: "gguf blob url");

            yield return new((
                "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/blob/main/model.safetensors",
                "https://huggingface.co", "sentence-transformers/all-MiniLM-L6-v2", "main", "model.safetensors", ModelKind.Safetensors
            ), DisplayName: "safetensors blob url");

            yield return new((
                "https://huggingface.co/owner/repo/blob/v1.0/model.gguf",
                "https://huggingface.co", "owner/repo", "v1.0", "model.gguf", ModelKind.Gguf
            ), DisplayName: "blob url with explicit revision");

            yield return new((
                "https://hf.mycorp.internal/team/model/blob/main/model.onnx",
                "https://hf.mycorp.internal", "team/model", "main", "model.onnx", ModelKind.Onnx
            ), DisplayName: "custom hub endpoint");
        }
    }

    public sealed class RejectedUrlCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("not-a-url", DisplayName: "not an absolute url");
            yield return new("https://huggingface.co/owner/repo", DisplayName: "bare owner/repo, no file");
            yield return new("https://huggingface.co/Xenova/multilingual-e5-small/tree/main", DisplayName: "tree url, no file");
            yield return new("https://huggingface.co/Xenova/multilingual-e5-small/tree/main/onnx", DisplayName: "tree url with subdir, no file");
            yield return new("https://huggingface.co/Xenova/multilingual-e5-small/blob/main/config.json", DisplayName: "blob url to a non-model file");
        }
    }

    #endregion // Test Case Generators

    [Test]
    [ModelFileUrlCases]
    public async ValueTask derives_fields_from_a_direct_file_url(
        string url, string expectedEndpoint, string expectedRepo, string expectedRevision, string expectedFilePath, ModelKind expectedKind
    ) {
        // Act
        var reference = ModelRef.From(url);

        // Assert
        await Assert.That(reference.Endpoint).IsEqualTo(expectedEndpoint);
        await Assert.That(reference.Repo).IsEqualTo(expectedRepo);
        await Assert.That(reference.Revision).IsEqualTo(expectedRevision);
        await Assert.That(reference.FilePath).IsEqualTo(expectedFilePath);
        await Assert.That(reference.Kind).IsEqualTo(expectedKind);
    }

    [Test]
    public async ValueTask builds_resolve_download_url_from_a_blob_url() {
        // Arrange — the raw download URL is the blob URL with /blob/ swapped for /resolve/.
        var reference = new ModelRef("https://huggingface.co/Xenova/multilingual-e5-small/blob/main/onnx/model_quantized.onnx");
        var expected  = "https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx";

        // Act
        var downloadUrl = reference.ResolveUrl(reference.FilePath);

        // Assert
        await Assert.That(downloadUrl).IsEqualTo(expected);
    }

    [Test]
    [RejectedUrlCases]
    public void rejects_anything_other_than_a_direct_model_file_url(string url) {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ModelRef(url));
    }
}
