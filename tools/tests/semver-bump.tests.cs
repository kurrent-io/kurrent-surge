#!/usr/bin/env dotnet
#:package TUnit@1.*
#:package CliWrap@3.*

using System.Runtime.CompilerServices;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[NotInParallel]
public class SemVerBumpTests {
    static readonly string Token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                                ?? Environment.GetEnvironmentVariable("GITHUB_PAT")
                                ?? "";
    
    static readonly string Repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "";

    static readonly string ToolPath = Path.GetFullPath(
        Path.Combine(GetSourceDir(), "..", "semver-bump.cs")
    );

    static string GetSourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [Before(Class)]
    public static async Task WarmUpToolBuild() {
        // Pre-build the tool so subsequent runs don't emit build warnings to stdout
        await Cli.Wrap("dotnet")
            .WithArguments(a => { a.Add("run"); a.Add(ToolPath); a.Add("--"); a.Add("--help"); })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
    }

    [Test]
    public async Task check_default_sets_env_vars() {
        // act
        var result = await Execute("check", "--no-progress");

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task check_json_output_contains_expected_fields() {
        // act
        var result = await Execute("check", "--output", "json", "--no-progress");
        var json = JsonDocument.Parse(result.StandardOutput.Trim());

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(json.RootElement.TryGetProperty("version", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("tag", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("bump", out _)).IsTrue();
    }

    [Test]
    public async Task check_github_output_writes_to_env_files() {
        // arrange
        var envFile     = Path.GetTempFileName();
        var outputFile  = Path.GetTempFileName();
        var summaryFile = Path.GetTempFileName();
        var envOverrides = new Dictionary<string, string> {
            ["GITHUB_ENV"]          = envFile,
            ["GITHUB_OUTPUT"]       = outputFile,
            ["GITHUB_STEP_SUMMARY"] = summaryFile
        };

        try {
            // act
            var result = await Execute(Token, Repository, ["check", "--output", "github", "--no-progress"], envOverrides);
            var envContent     = await File.ReadAllTextAsync(envFile);
            var outputContent  = await File.ReadAllTextAsync(outputFile);
            var summaryContent = await File.ReadAllTextAsync(summaryFile);

            // assert
            await Assert.That(result.ExitCode).IsEqualTo(0);

            await Assert.That(envContent).Contains("BUMP_VERSION=");
            await Assert.That(envContent).Contains("BUMP_TAG=");
            await Assert.That(envContent).Contains("BUMP_TYPE=");

            await Assert.That(outputContent).Contains("version=");
            await Assert.That(outputContent).Contains("tag=");
            await Assert.That(outputContent).Contains("bump=");

            await Assert.That(summaryContent).Contains("## Version Bump");
        } finally {
            File.Delete(envFile);
            File.Delete(outputFile);
            File.Delete(summaryFile);
        }
    }

    [Test]
    public async Task check_github_fails_without_env_files() {
        // arrange
        var envOverrides = new Dictionary<string, string> {
            ["GITHUB_ENV"]          = "",
            ["GITHUB_OUTPUT"]       = "",
            ["GITHUB_STEP_SUMMARY"] = ""
        };

        // act
        var result = await Execute(Token, Repository, ["check", "--output", "github"], envOverrides);

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).Contains("GITHUB_ENV");
    }

    [Test]
    public async Task check_fails_with_missing_token() {
        // act
        var result = await Execute("", Repository, ["check"]);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task check_fails_with_missing_repository() {
        // act
        var result = await Execute(Token, "", ["check"]);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task bump_fails_with_missing_token() {
        // act
        var result = await Execute("", Repository, ["bump"]);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task bump_fails_with_missing_repository() {
        // act
        var result = await Execute(Token, "", ["bump"]);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    static Task<BufferedCommandResult> Execute(params string[] args) =>
        Execute(Token, Repository, args);

    static async Task<BufferedCommandResult> Execute(
        string token, string repo, string[] args, Dictionary<string, string>? envOverrides = null
    ) {
        var cmd = Cli.Wrap("dotnet")
            .WithArguments(a => {
                a.Add("run");
                a.Add(ToolPath);
                a.Add("--");
                a.Add("--token"); a.Add(token);
                a.Add("--repo"); a.Add(repo);
                foreach (var arg in args) a.Add(arg);
            })
            .WithEnvironmentVariables(e => {
                if (envOverrides is null)
                    return;

                foreach (var (key, value) in envOverrides)
                    e.Set(key, value);
            })
            .WithValidation(CommandResultValidation.None);

        return await cmd.ExecuteBufferedAsync();
    }
}
