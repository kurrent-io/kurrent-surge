using Kurrent.Testkit.TUnit;

namespace Kurrent.Surge.Cli.Tests;

/// <summary>Local, no-network tests for the <c>surge huggingface</c> command group, invoked in-process via <see cref="CliApp"/>.</summary>
[Timeout(10_000)]
public sealed class CliLocalTests {
    [Test]
    public async ValueTask probe_prints_arch_tags() {
        // Arrange
        var console = new TestConsole();

        // Act
        var exitCode = await CliApp.Build(console).Parse(["huggingface", "probe"]).InvokeAsync();

        // Assert
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(console.Output).IsNotEmpty();
    }

    [Test]
    public async ValueTask cache_without_subcommand_prints_the_resolved_root() {
        // Arrange — captures the raw output writer, not TestConsole: the bare `cache` invocation bypasses
        // Spectre entirely (it's machine-consumable, meant for shell substitution), so Spectre rendering isn't
        // under test here. TestConsole defaults to an 80-column width; routing this through IAnsiConsole would
        // silently wrap a long path onto a second line and corrupt it — exactly the defect this test exists to catch.
        var output                 = new StringWriter();
        var cacheDirectory         = TestTemp.NewDirectory();
        var expectedCacheDirectory = Path.GetFullPath(cacheDirectory); // mirrors HuggingFaceHub.ResolveCacheRoot
        try {
            // Act
            var exitCode = await CliApp.Build(output: output).Parse(["huggingface", "cache", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(output.ToString().Trim()).IsEqualTo(expectedCacheDirectory);
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    public sealed class UnknownCommandCases : TestCaseDataGenerator<string[]> {
        protected override IEnumerable<TestCase<string[]>> Data() {
            yield return new(["not-a-real-command"], DisplayName: "unknown top-level command");
            yield return new(["huggingface", "models", "not-a-real-subcommand"], DisplayName: "unknown models subcommand");
            yield return new(["huggingface", "cache", "not-a-real-subcommand"], DisplayName: "unknown cache subcommand");
        }
    }

    [Test]
    [UnknownCommandCases]
    public async ValueTask unknown_command_fails_with_nonzero_exit(string[] args) {
        // Arrange
        var console = new TestConsole();

        // Act
        var exitCode = await CliApp.Build(console).Parse(args).InvokeAsync();

        // Assert
        await Assert.That(exitCode).IsNotEqualTo(0);
    }

    [Test]
    public async ValueTask cache_list_shows_cached_models() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main");
            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console).Parse(["huggingface", "cache", "list", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(console.Output).Contains("Xenova/all-MiniLM-L6-v2");
            await Assert.That(console.Output).Contains("main");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask cache_list_reports_an_empty_cache() {
        // Arrange
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console).Parse(["huggingface", "cache", "list", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(console.Output).Contains("No cached models");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask cache_clear_with_yes_removes_only_model_directories() {
        // Arrange — a real cached model plus a decoy file and a decoy directory in the cache root; only the
        // model directory ListCachedModels() actually reported may be deleted.
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var modelDirectory = CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main");
            var decoyFile      = Path.Combine(cacheDirectory, "README.txt");
            var decoyDirectory = Path.Combine(cacheDirectory, "not-a-model-dir");
            File.WriteAllText(decoyFile, "keep me");
            Directory.CreateDirectory(decoyDirectory);

            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console).Parse(["huggingface", "cache", "clear", "--yes", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(Directory.Exists(modelDirectory)).IsFalse();
            await Assert.That(File.Exists(decoyFile)).IsTrue();
            await Assert.That(Directory.Exists(decoyDirectory)).IsTrue();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask cache_clear_without_yes_refuses_when_non_interactive() {
        // Arrange — a fresh TestConsole defaults to non-interactive (Profile.Capabilities.Interactive ==
        // false), which is the refusal path this test can actually exercise; the interactive confirmation
        // prompt itself isn't testable this way (per the task — not attempted).
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var modelDirectory = CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main");
            var console        = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console).Parse(["huggingface", "cache", "clear", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert — refused, nothing deleted.
            await Assert.That(exitCode).IsNotEqualTo(0);
            await Assert.That(Directory.Exists(modelDirectory)).IsTrue();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    [RequiresDirectoryPermissionEnforcement]
    public async ValueTask cache_clear_reports_partially_failed_deletions() {
        // Arrange — one deletable model, one "locked" model: its snapshot directory's write bit is removed
        // (POSIX only — see RequiresDirectoryPermissionEnforcement), so Directory.Delete can enumerate its
        // file but can't unlink it, throwing UnauthorizedAccessException partway through that model's delete.
        var cacheDirectory    = TestTemp.NewDirectory();
        var deletableModel    = CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main");
        var lockedModel       = CreateFabricatedSnapshot(cacheDirectory, "acme", "locked-model", "main");
        var lockedSnapshotDir = Path.Combine(lockedModel, "snapshots", "main");

        File.SetUnixFileMode(lockedSnapshotDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try {
            var console      = new TestConsole();
            var errorConsole = new TestConsole();
            errorConsole.Profile.Width = 4096; // decorative error text wraps by design at the real terminal width; wide here so the assertion sees it unwrapped.

            // Act — the failure line is product-correct on errorConsole (stderr), not stdout; captured separately.
            var exitCode = await CliApp.Build(console, errorConsole).Parse(["huggingface", "cache", "clear", "--yes", "--cache-dir", cacheDirectory]).InvokeAsync();

            // Assert — the deletable model is gone, the locked one survives, failure is reported, exit 1.
            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(Directory.Exists(deletableModel)).IsFalse();
            await Assert.That(Directory.Exists(lockedModel)).IsTrue();
            await Assert.That(errorConsole.Output).Contains(lockedModel);
            await Assert.That(console.Output).Contains("Removed 1 of 2");
        } finally {
            // Restore the write bit so TestTemp.Cleanup can actually remove the temp root afterward.
            File.SetUnixFileMode(lockedSnapshotDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask models_clear_deletes_only_the_named_model() {
        // Arrange — two cached models plus a decoy file in the root; clearing one must leave the other and
        // the decoy untouched.
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var removedModel  = CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "main");
            var survivorModel = CreateFabricatedSnapshot(cacheDirectory, "acme", "other-model", "main");
            var decoyFile     = Path.Combine(cacheDirectory, "README.txt");
            File.WriteAllText(decoyFile, "keep me");

            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console)
                .Parse(["huggingface", "models", "clear", "Xenova/all-MiniLM-L6-v2", "--cache-dir", cacheDirectory])
                .InvokeAsync();

            // Assert
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(Directory.Exists(removedModel)).IsFalse();
            await Assert.That(Directory.Exists(survivorModel)).IsTrue();
            await Assert.That(File.Exists(decoyFile)).IsTrue();
            await Assert.That(console.Output).Contains("Xenova/all-MiniLM-L6-v2");
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask models_clear_removes_every_revision_of_the_model() {
        // Arrange — one model, two cached revisions.
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var modelDirectory = CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "v1");
            CreateFabricatedSnapshot(cacheDirectory, "Xenova", "all-MiniLM-L6-v2", "v2");

            var console = new TestConsole();

            // Act
            var exitCode = await CliApp.Build(console)
                .Parse(["huggingface", "models", "clear", "Xenova/all-MiniLM-L6-v2", "--cache-dir", cacheDirectory])
                .InvokeAsync();

            // Assert — the whole model directory (both revisions) is gone in one shot.
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(Directory.Exists(modelDirectory)).IsFalse();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    [Test]
    public async ValueTask models_clear_fails_for_an_unknown_model() {
        // Arrange — an unrelated cached model; the requested repo was never cached.
        var cacheDirectory = TestTemp.NewDirectory();
        try {
            var unrelatedModel = CreateFabricatedSnapshot(cacheDirectory, "acme", "other-model", "main");
            var console        = new TestConsole();
            var errorConsole   = new TestConsole();
            errorConsole.Profile.Width = 4096; // decorative error text wraps by design; wide so the assertion sees it unwrapped.

            // Act
            var exitCode = await CliApp.Build(console, errorConsole)
                .Parse(["huggingface", "models", "clear", "Xenova/all-MiniLM-L6-v2", "--cache-dir", cacheDirectory])
                .InvokeAsync();

            // Assert — refused, nothing deleted.
            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(errorConsole.Output).Contains("Xenova/all-MiniLM-L6-v2");
            await Assert.That(Directory.Exists(unrelatedModel)).IsTrue();
        } finally {
            TestTemp.Cleanup(cacheDirectory);
        }
    }

    /// <summary>
    /// Skips on Windows: removing a directory's write bit reliably blocks deletion on POSIX (macOS/Linux —
    /// this repo's actual dev/CI hosts), but <see cref="File.SetUnixFileMode"/> throws
    /// <see cref="PlatformNotSupportedException"/> there rather than modeling a real block, and Windows has
    /// no equivalent in-process, reliable, non-flaky lock available to this test.
    /// </summary>
    public sealed class RequiresDirectoryPermissionEnforcementAttribute() : SkipAttribute(
        "Directory write-permission bits aren't a reliable delete-block on Windows (SetUnixFileMode is POSIX-only).") {
        public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(OperatingSystem.IsWindows());
    }

    // Creates {cacheRoot}/models--{owner}--{name}/snapshots/{revision}/file.bin and returns the model's root
    // (parent) directory — the one `cache clear` / `models clear` deletes.
    static string CreateFabricatedSnapshot(string cacheRoot, string owner, string name, string revision) {
        var modelDirectory = Path.Combine(cacheRoot, $"models--{owner}--{name}");
        var snapshotDir    = Path.Combine(modelDirectory, "snapshots", revision);
        Directory.CreateDirectory(snapshotDir);
        File.WriteAllBytes(Path.Combine(snapshotDir, "file.bin"), [1, 2, 3, 4]);
        return modelDirectory;
    }
}
