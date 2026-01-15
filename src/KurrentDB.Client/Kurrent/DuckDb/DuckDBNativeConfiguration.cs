using System.Collections.Frozen;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;
using static System.StringComparer;

namespace Kurrent.DuckDB;

/// <summary>
/// Represents a DuckDB configuration flag with its name and description.
/// </summary>
sealed record DuckDBNativeConfigurationFlag(string Name, string Description);

/// <summary>
/// Provides access to DuckDB's native configuration options.
/// Useful for validation and discovery of available configuration keys.
/// </summary>
static class DuckDBNativeConfiguration {
    static readonly FrozenDictionary<string, DuckDBNativeConfigurationFlag> FlagsByName = LoadFlags();

    /// <summary>
    /// Gets all available configuration flags from the native DuckDB library.
    /// </summary>
    public static IReadOnlyCollection<DuckDBNativeConfigurationFlag> Flags => FlagsByName.Values;

    /// <summary>
    /// Checks if a configuration key is valid (recognized by DuckDB).
    /// </summary>
    public static bool IsValidKey(string? key) =>
        key is not null && FlagsByName.ContainsKey(key);

    /// <summary>
    /// Gets the description for a configuration key.
    /// </summary>
    public static string GetDescription(string key) =>
        FlagsByName.TryGetValue(key, out var flag) ? flag.Description : "";

    /// <summary>
    /// Suggests a similar valid key for a potentially misspelled key.
    /// </summary>
    public static string? SuggestSimilar(string invalidKey, int maxDistance = 3) {
        string? bestMatch    = null;
        var     bestDistance = int.MaxValue;

        foreach (var flag in FlagsByName.Values) {
            var distance = ComputeLevenshteinDistance(invalidKey, flag.Name);
            if (distance >= bestDistance || distance > maxDistance) continue;

            bestDistance = distance;
            bestMatch    = flag.Name;
        }

        return bestMatch;

        static int ComputeLevenshteinDistance(string source, string target) {
            if (string.IsNullOrEmpty(source))
                return string.IsNullOrEmpty(target) ? 0 : target.Length;

            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;

            var previousRow = new int[targetLength + 1];
            var currentRow  = new int[targetLength + 1];

            for (var j = 0; j <= targetLength; j++)
                previousRow[j] = j;

            for (var i = 1; i <= sourceLength; i++) {
                currentRow[0] = i;
                var sourceChar = char.ToLowerInvariant(source[i - 1]);

                for (var j = 1; j <= targetLength; j++) {
                    var targetChar = char.ToLowerInvariant(target[j - 1]);
                    var cost       = sourceChar == targetChar ? 0 : 1;

                    currentRow[j] = Math.Min(
                        Math.Min(
                            previousRow[j] + 1,
                            currentRow[j - 1] + 1
                        ),
                        previousRow[j - 1] + cost
                    );
                }

                (previousRow, currentRow) = (currentRow, previousRow);
            }

            return previousRow[targetLength];
        }
    }

    /// <summary>
    /// Validates a collection of configuration keys and returns any invalid keys.
    /// </summary>
    public static IReadOnlyList<string> GetInvalidKeys(IEnumerable<string> keys) =>
        keys.Where(key => !IsValidKey(key)).ToArray();

    static FrozenDictionary<string, DuckDBNativeConfigurationFlag> LoadFlags() {
        var count = NativeMethods.Configuration.DuckDBConfigCount();
        var flags = new Dictionary<string, DuckDBNativeConfigurationFlag>(count, OrdinalIgnoreCase);

        for (var i = 0; i < count; i++) {
            var state = NativeMethods.Configuration.DuckDBGetConfigFlag(i, out var namePtr, out var descPtr);
            if (state != DuckDBState.Success)
                continue;

            var name = Marshal.PtrToStringUTF8(namePtr);
            if (string.IsNullOrEmpty(name))
                continue;

            flags[name] = new(name, Marshal.PtrToStringUTF8(descPtr) ?? "");
        }

        return flags.ToFrozenDictionary(OrdinalIgnoreCase);
    }
}
