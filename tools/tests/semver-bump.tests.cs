#!/usr/bin/env dotnet
#:package TUnit@1.*
#:package CliWrap@3.*

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;

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
    public async Task check_default_output_returns_bare_version() {
        // act
        var result = await Execute("check", "--no-progress");
        var stdout = result.StandardOutput.Trim();

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(stdout).IsNotEmpty();
        await Assert.That(Regex.IsMatch(stdout, @"^\d+\.\d+\.\d+$"))
            .IsTrue()
            .Because($"Expected bare version, got: {stdout}");
    }

    [Test]
    public async Task check_kv_output_contains_expected_keys() {
        // act
        var result = await Execute("check", "--output", "kv", "--no-progress");

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("version=");
        await Assert.That(result.StandardOutput).Contains("tag=");
        await Assert.That(result.StandardOutput).Contains("bump=");
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
    public async Task check_github_output_contains_expected_keys() {
        // act
        var result = await Execute("check", "--output", "github", "--no-progress");

        // assert
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("version=");
        await Assert.That(result.StandardOutput).Contains("tag=");
        await Assert.That(result.StandardOutput).Contains("bump=");
    }

    [Test]
    public async Task check_fails_with_missing_token() {
        // arrange
        var envOverrides = new Dictionary<string, string> { ["GITHUB_TOKEN"] = "" };

        // act
        var result = await Execute(new[] { "check" }, envOverrides);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task check_fails_with_missing_repository() {
        // arrange
        var envOverrides = new Dictionary<string, string> { ["GITHUB_REPOSITORY"] = "" };

        // act
        var result = await Execute(new[] { "check" }, envOverrides);

        // assert
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    static Task<BufferedCommandResult> Execute(params string[] args) =>
        Execute(args, null);

    static async Task<BufferedCommandResult> Execute(string[] args, Dictionary<string, string>? envOverrides) {
        var cmd = Cli.Wrap("dotnet")
            .WithArguments(a => {
                a.Add("run");
                a.Add(ToolPath);
                a.Add("--");
                foreach (var arg in args) a.Add(arg);
            })
            .WithEnvironmentVariables(e => {
                e.Set("GITHUB_TOKEN", Token);
                e.Set("GITHUB_REPOSITORY", Repository);
                if (envOverrides is not null) {
                    foreach (var (key, value) in envOverrides)
                        e.Set(key, value);
                }
            })
            .WithValidation(CommandResultValidation.None);

        return await cmd.ExecuteBufferedAsync();
    }
}
