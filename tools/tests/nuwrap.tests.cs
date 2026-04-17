#!/usr/bin/env dotnet
#:package TUnit@1.*
#:package CliWrap@3.*

using System.IO.Compression;
using System.Runtime.CompilerServices;
using CliWrap;
using CliWrap.Buffered;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[NotInParallel]
public class NuWrapTests {
    static readonly string ToolPath = Path.GetFullPath(
        Path.Combine(GetSourceDir(), "..", "nuwrap.cs")
    );

    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [Before(Class)]
    public static async Task WarmUpToolBuild() {
        // Pre-build the tool so subsequent test runs don't emit build warnings to stdout
        await Cli.Wrap("dotnet")
            .WithArguments(a => { a.Add("run"); a.Add(ToolPath); a.Add("--"); a.Add("--help"); })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
    }

    #region ->> CLI validation <<-

    [Test]
    public async Task help_exits_zero_and_lists_options() {
        var result = await Execute("--help");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("--target-version");
        await Assert.That(result.StandardOutput).Contains("--source");
        await Assert.That(result.StandardOutput).Contains("--push");
    }

    [Test]
    public async Task missing_target_version_exits_nonzero() {
        // no --no-progress so validation errors appear on stderr
        var result = await Execute(
            "--source", "/tmp/nonexistent",
            "--push", "/tmp/nonexistent"
        );

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.StandardError).Contains("--target-version is required");
    }

    [Test]
    public async Task invalid_version_exits_nonzero() {
        var result = await Execute(
            "--target-version", "not-a-version",
            "--source", "/tmp/nonexistent"
        );

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.StandardError).Contains("Invalid version");
    }

    [Test]
    public async Task missing_source_and_push_exits_nonzero() {
        var result = await Execute("--target-version", "2.0.0");

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.StandardError).Contains("--source or --push");
    }

    #endregion

    #region ->> Config loading <<-

    [Test]
    public async Task config_file_loads_version_and_force() {
        var tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            Directory.CreateDirectory(tmpDir);
            var configPath = Path.Combine(tmpDir, "nuwrap.yml");
            await File.WriteAllTextAsync(configPath, "version: \"2.0.0\"\nforce: true\n");

            // Package at 3.0.0-preview.1 would normally reject 2.0.0 as a downgrade,
            // but force=true from the config file bypasses the check.
            await CreateFixtureNupkg("Config.Test", "3.0.0-preview.1", tmpDir);

            var result = await Execute(
                "--no-progress",
                "--config", configPath,
                "--dry-run",
                "--source", tmpDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);
        } finally {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Test]
    public async Task cli_target_version_overrides_config_file() {
        var tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            Directory.CreateDirectory(tmpDir);
            var configPath = Path.Combine(tmpDir, "nuwrap.yml");
            await File.WriteAllTextAsync(configPath, "version: \"2.0.0\"\nforce: true\n");

            await CreateFixtureNupkg("Override.Test", "1.0.0-preview.1", tmpDir);

            // config file has version=2.0.0, but CLI overrides to 3.0.0
            var result = await Execute(
                "--config", configPath,
                "--dry-run",
                "--target-version", "3.0.0",
                "--source", tmpDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardError).Contains("3.0.0");
        } finally {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    #endregion

    #region ->> Dry run <<-

    [Test]
    public async Task dry_run_with_local_source_shows_plan() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            await CreateFixtureNupkg("My.Package", "1.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--target-version", "2.0.0",
                "--dry-run",
                "--source", sourceDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardError).Contains("My.Package");
            await Assert.That(result.StandardError).Contains("2.0.0");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
        }
    }

    [Test]
    public async Task dry_run_warns_on_prerelease_target_version() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            await CreateFixtureNupkg("My.Package", "1.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--target-version", "2.0.0-rc.1",
                "--dry-run",
                "--source", sourceDir
            );

            await Assert.That(result.StandardError).Contains("pre-release");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
        }
    }

    #endregion

    #region ->> Stamping <<-

    [Test]
    public async Task stamps_version_in_nuspec() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            await CreateFixtureNupkg("Stamp.Test", "1.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--no-progress",
                "--target-version", "2.0.0",
                "--source", sourceDir,
                "--output", outputDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);

            var stamped = Directory.GetFiles(outputDir, "*.nupkg");
            await Assert.That(stamped.Length).IsEqualTo(1);
            await Assert.That(Path.GetFileName(stamped[0])).Contains("2.0.0");

            // Verify nuspec inside the stamped nupkg has the new version
            await using var zip = await ZipFile.OpenReadAsync(stamped[0]);
            var nuspecEntry = zip.Entries.First(e => e.FullName.EndsWith(".nuspec"));
            await using var stream = await nuspecEntry.OpenAsync();
            var doc     = await System.Xml.Linq.XDocument.LoadAsync(stream, System.Xml.Linq.LoadOptions.None, default);
            var ns      = doc.Root!.GetDefaultNamespace();
            var version = doc.Root.Element(ns + "metadata")!.Element(ns + "version")!.Value;
            await Assert.That(version).IsEqualTo("2.0.0");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Test]
    public async Task stamps_sibling_dependency_versions() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            // Sibling.A depends on Sibling.B — both are in the same batch
            await CreateFixtureNupkg("Sibling.A", "1.0.0-preview.1", sourceDir, dep: ("Sibling.B", "1.0.0-preview.1"));
            await CreateFixtureNupkg("Sibling.B", "1.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--no-progress",
                "--target-version", "2.0.0",
                "--source", sourceDir,
                "--output", outputDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);

            var stampedA = Directory.GetFiles(outputDir, "Sibling.A.2.0.0.nupkg").FirstOrDefault();
            await Assert.That(stampedA).IsNotNull();

            await using var zip = await ZipFile.OpenReadAsync(stampedA!);
            var nuspecEntry = zip.Entries.First(e => e.FullName.EndsWith(".nuspec"));
            await using var stream = await nuspecEntry.OpenAsync();
            var doc = await System.Xml.Linq.XDocument.LoadAsync(stream, System.Xml.Linq.LoadOptions.None, default);
            var ns  = doc.Root!.GetDefaultNamespace();
            var depVersion = doc.Root
                .Element(ns + "metadata")!
                .Element(ns + "dependencies")!
                .Descendants(ns + "dependency")
                .First(d => d.Attribute("id")?.Value == "Sibling.B")
                .Attribute("version")!.Value;
            await Assert.That(depVersion).IsEqualTo("2.0.0");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    #endregion

    #region ->> Push <<-

    [Test]
    public async Task push_copies_stamped_packages_to_local_directory() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var pushDir   = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            await CreateFixtureNupkg("Push.Test", "1.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--no-progress",
                "--target-version", "2.0.0",
                "--source", sourceDir,
                "--output", outputDir,
                "--push", pushDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);

            var pushed = Directory.GetFiles(pushDir, "*.nupkg");
            await Assert.That(pushed.Length).IsEqualTo(1);
            await Assert.That(Path.GetFileName(pushed[0])).Contains("2.0.0");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
            if (Directory.Exists(pushDir))   Directory.Delete(pushDir,   recursive: true);
        }
    }

    #endregion

    #region ->> Error cases <<-

    [Test]
    public async Task rejects_downgrade_without_force() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            // Package is at 3.0.0-preview.1; stamping with 2.0.0 is a downgrade
            await CreateFixtureNupkg("Downgrade.Test", "3.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--target-version", "2.0.0",
                "--source", sourceDir,
                "--output", outputDir
            );

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.StandardError).Contains("--force to bypass");
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    [Test]
    public async Task force_allows_downgrade() {
        var sourceDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try {
            // Package is at 3.0.0-preview.1; --force bypasses the downgrade check
            await CreateFixtureNupkg("Force.Test", "3.0.0-preview.1", sourceDir);

            var result = await Execute(
                "--no-progress",
                "--target-version", "2.0.0",
                "--force",
                "--source", sourceDir,
                "--output", outputDir
            );

            await Assert.That(result.ExitCode).IsEqualTo(0);
        } finally {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, recursive: true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        }
    }

    #endregion

    #region ->> Helpers <<-

    static async Task<FileInfo> CreateFixtureNupkg(
        string id, string version, string directory,
        (string id, string version)? dep = null
    ) {
        Directory.CreateDirectory(directory);

        var depXml = dep is null ? "" : $"""
                    <dependencies>
                      <dependency id="{dep.Value.id}" version="{dep.Value.version}" />
                    </dependencies>
            """;

        var nuspec = $"""
            <?xml version="1.0"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <description>Test fixture package</description>
                <authors>Test</authors>
                {depXml}
              </metadata>
            </package>
            """;

        var tmpDir  = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var outPath = Path.Combine(directory, $"{id}.{version}.nupkg");
        try {
            Directory.CreateDirectory(tmpDir);
            await File.WriteAllTextAsync(Path.Combine(tmpDir, $"{id}.nuspec"), nuspec);
            await ZipFile.CreateFromDirectoryAsync(tmpDir, outPath);
        } finally {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }

        return new FileInfo(outPath);
    }

    static Task<BufferedCommandResult> Execute(params string[] args) =>
        Cli.Wrap("dotnet")
            .WithArguments(a => {
                a.Add("run");
                a.Add(ToolPath);
                a.Add("--");
                foreach (var arg in args) a.Add(arg);
            })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

    #endregion
}
