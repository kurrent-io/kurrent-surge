using Microsoft.Extensions.Configuration;
using static System.String;

namespace Kurrent.Testing.Configuration;

[PublicAPI]
public static class ConfigurationExtensions {
    extension(IConfiguration configuration) {
        public T GetOptionsOrDefault<T>(string sectionName) where T : new() {
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
            var options = configuration
                .GetSection(sectionName)
                .Get<T>(options => options.BindNonPublicProperties = true);
            return options ?? new T();
        }

        public T GetOptionsOrDefault<T>() where T : new() =>
            configuration.Get<T>(options => options.BindNonPublicProperties = true) ?? new T();

        public T GetRequiredOptions<T>() {
            var options = configuration.Get<T>(options => options.BindNonPublicProperties = true);
            return options ?? throw new InvalidOperationException($"Failed to load options from configuration: {typeof(T).Name}");
        }

        public T GetRequiredOptions<T>(string sectionName) {
            ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
            var options = configuration.GetRequiredSection(sectionName).Get<T>(options => options.BindNonPublicProperties = true);
            return options ?? throw new InvalidOperationException($"Failed to load options from configuration: {typeof(T).Name}");
        }

        public string[] Values(string key, char separator = ',') {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var value = configuration.GetValue(key, Empty);

            return IsNullOrWhiteSpace(value)
                ? []
                : value.Contains(separator)
                    ? value.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : [value];
        }

        public T Value<T>(string key, T defaultValue) {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return configuration.GetValue(key, defaultValue)!;
        }

        public T GetFirstValue<T>(string[] keys, T defaultValue) {
            ArgumentOutOfRangeException.ThrowIfZero(keys.Length);
            ArgumentNullException.ThrowIfNull(defaultValue);

            T? value = default;
            while (value is null && keys.Length > 0) {
                value = configuration.GetValue<T?>(keys[0], default);
                keys  = keys[1..];
            }

            return value ?? defaultValue;
        }
    }
}

[PublicAPI]
public static class ConfigurationSettingsExtensions {
    extension(IDictionary<string, string?> settings) {
        public IConfiguration ToConfiguration() =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    extension(IConfiguration configuration) {
        public IDictionary<string, string?> ToSettings() =>
            new Dictionary<string, string?>(configuration.AsEnumerable().Where(x => x.Value is not null));
    }
}

//
// public static class EnvironmentVariablesExtensions {
//     // For IConfigurationBuilder (existing method)
//     public static IConfigurationBuilder AddNormalizedEnvironmentVariables(
//         this IConfigurationBuilder builder,
//         string? prefix = null,
//         bool overrideExisting = true
//     ) {
//         var config = BuildNormalizedConfiguration(prefix);
//         ApplyConfiguration(builder, config, overrideExisting);
//         return builder;
//     }
//
//     // public static IConfiguration AddNormalizedEnvironmentVariables(
//     //     this IConfiguration configuration,
//     //     string? prefix = null,
//     //     bool overrideExisting = true
//     // ) {
//     //     var builder = new ConfigurationBuilder();
//     //
//     //     // Add existing configuration sources
//     //     if (configuration is IConfigurationRoot root)
//     //         foreach (var provider in root.Providers)
//     //             // Wrap existing providers to preserve them
//     //             builder.Add(new ExistingProviderConfigurationSource(provider));
//     //     else
//     //         // Fallback: add as in-memory collection
//     //         builder.AddInMemoryCollection(
//     //             configuration.AsEnumerable()
//     //                 .Where(kvp => kvp.Value is not null)
//     //                 .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)!
//     //         );
//     //
//     //     // Add normalized environment variables
//     //     var normalizedConfig = BuildNormalizedConfiguration(prefix);
//     //
//     //     if (overrideExisting) {
//     //         builder.AddInMemoryCollection(normalizedConfig!);
//     //     }
//     //     else {
//     //         // Insert at the beginning
//     //         var existingSources = builder.Sources.ToList();
//     //         builder.Sources.Clear();
//     //         builder.AddInMemoryCollection(normalizedConfig!);
//     //         foreach (var source in existingSources)
//     //             builder.Sources.Add(source);
//     //     }
//     //
//     //     return builder.Build();
//     // }
//
//     static Dictionary<string, string?> BuildNormalizedConfiguration(string? prefix) {
//         return Environment.GetEnvironmentVariables()
//             .Cast<DictionaryEntry>()
//             .Where(entry => MatchesPrefix(entry, prefix))
//             .Select(ToKeyValue)
//             .Select(x => NormalizeKey(x, prefix))
//             .GroupBy(x => x.NormalizedKey, StringComparer.OrdinalIgnoreCase)
//             .ToDictionary(
//                 g => g.Key,
//                 g => g.MaxBy(x => x.HasDoubleUnderscore).Value,
//                 StringComparer.OrdinalIgnoreCase
//             );
//
//         static bool MatchesPrefix(DictionaryEntry entry, string? prefix) =>
//             prefix is null || ((string)entry.Key).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
//
//         static (string Key, string? Value) ToKeyValue(DictionaryEntry entry) =>
//             ((string)entry.Key, entry.Value?.ToString());
//
//         static (string NormalizedKey, string? Value, bool HasDoubleUnderscore) NormalizeKey(
//             (string Key, string? Value) x, string? prefix) => (
//             NormalizedKey: prefix is null
//                 ? x.Key.Replace("_", "__")
//                 : x.Key.Replace("_", "__")[prefix.Length..],
//             Value: x.Value,
//             HasDoubleUnderscore: x.Key.Contains("__")
//         );
//     }
//
//     static void ApplyConfiguration(IConfigurationBuilder builder, Dictionary<string, string?> config, bool overrideExisting) {
//         if (overrideExisting)
//             builder.AddInMemoryCollection(config);
//         else {
//             var existingSources = builder.Sources.ToList();
//             builder.Sources.Clear();
//             builder.AddInMemoryCollection(config);
//             foreach (var source in existingSources)
//                 builder.Sources.Add(source);
//         }
//     }
//
//     // Helper to wrap existing configuration providers
//     class ExistingProviderConfigurationSource(IConfigurationProvider provider) : IConfigurationSource {
//         public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
//     }
// }
