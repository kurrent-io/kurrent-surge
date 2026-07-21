using System.Text.Json.Serialization;

namespace Kurrent.Surge.Cli;

// The --json shape for `inspect`: { repo, revision, kind, url, cached, totalBytes, files: [{ repoPath, localPath, size, sha256 }] }
sealed record InspectJsonResult(
    string Repo, string Revision, string Kind, string Url, bool Cached, long TotalBytes,
    IReadOnlyList<InspectJsonFile> Files);

sealed record InspectJsonFile(string RepoPath, string LocalPath, long Size, string? Sha256);

// Source-generated (AOT-safe) — no reflection-based JSON.
[JsonSerializable(typeof(InspectJsonResult))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
sealed partial class CliJsonContext : JsonSerializerContext;
