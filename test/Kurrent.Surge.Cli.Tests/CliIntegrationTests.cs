using System.Text.Json;

namespace Kurrent.Surge.Cli.Tests;

/// <summary>
/// Live integration tests for the <c>surge huggingface</c> command group against the <b>real</b> Hugging Face
/// Hub, invoked in-process via <see cref="CliApp"/> against a captured <see cref="TestConsole"/> — no spawned
/// process. Network required; <c>[Category("Integration")]</c>. Every command here takes <c>--cache-dir</c>
/// directly, so the cache root is redirected via the option rather than an environment variable — no
/// process-global state, so these tests don't need to run serially.
/// </summary>
[Timeout(120_000)]
public sealed class CliIntegrationTests {
    const string ModelUrl = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/blob/main/onnx/model_quantized.onnx";

    [Test]
    [Category("Integration")]
    public async ValueTask inspect_prints_the_bundle_table(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console)
                .Parse(["huggingface", "models", "inspect", ModelUrl, "--cache-dir", cacheDirectory])
                .InvokeAsync(cancellationToken: ct);

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(console.Output).Contains("model_quantized.onnx");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    [Category("Integration")]
    public async ValueTask inspect_json_emits_the_machine_shape(CancellationToken ct) {
        // Arrange — captures the raw output writer, not TestConsole: --json bypasses Spectre entirely (it's
        // machine-consumable, not decorative), so Spectre rendering isn't what's under test here.
        var cacheDirectory = TestTemp.NewDirectory();
        var output         = new StringWriter();
        const string expectedRepo = "Xenova/all-MiniLM-L6-v2";
        const string expectedFile = "onnx/model_quantized.onnx";
        try {
            // Act
            var exitCode = await CliApp.Build(output: output)
                .Parse(["huggingface", "models", "inspect", ModelUrl, "--json", "--cache-dir", cacheDirectory])
                .InvokeAsync(cancellationToken: ct);
            var payload = JsonSerializer.Deserialize(output.ToString(), CliJsonContext.Default.InspectJsonResult);

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(payload).IsNotNull();
            await Assert.That(payload!.Repo).IsEqualTo(expectedRepo);
            await Assert.That(payload.Files.Any(f => f.RepoPath == expectedFile)).IsTrue();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    [Category("Integration")]
    public async ValueTask download_downloads_every_file_to_disk(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console)
                .Parse(["huggingface", "models", "download", ModelUrl, "--cache-dir", cacheDirectory])
                .InvokeAsync(cancellationToken: ct);

            // Assert — every file the hub plans for this URL exists on disk at its expected size.
            var hub    = new HuggingFaceHub(new HubOptions { CacheDirectory = cacheDirectory });
            var bundle = await hub.InspectModelAsync(new ModelRef(ModelUrl), ct);

            await Assert.That(exitCode).IsEqualTo(0);
            foreach (var file in bundle.Files) {
                await Assert.That(File.Exists(file.LocalPath)).IsTrue();
                await Assert.That(new FileInfo(file.LocalPath).Length).IsEqualTo(file.Size);
            }
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    [Category("Integration")]
    public async ValueTask download_quiet_prints_only_the_summary(CancellationToken ct) {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        const int expectedLineCount = 1;
        try {
            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console)
                .Parse(["huggingface", "models", "download", ModelUrl, "--quiet", "--cache-dir", cacheDirectory])
                .InvokeAsync(cancellationToken: ct);

            // Assert — exactly one summary line, no progress-bar chrome.
            var lines = console.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(lines.Length).IsEqualTo(expectedLineCount);
            await Assert.That(lines[0]).Contains("Downloaded");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }
}
