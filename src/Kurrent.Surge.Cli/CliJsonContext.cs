using System.Text.Json.Serialization;

namespace Kurrent.Surge.Cli;

// Source-generated (AOT-safe) — no reflection-based JSON.
[JsonSerializable(typeof(InspectJsonResult))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
sealed partial class CliJsonContext : JsonSerializerContext;
