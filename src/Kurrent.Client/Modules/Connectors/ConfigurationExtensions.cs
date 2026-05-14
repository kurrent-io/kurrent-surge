using System.Globalization;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Connectors;

/// <summary>
/// Extension methods for converting between IConfiguration, JSON strings, and Protobuf Struct.
/// </summary>
public static class ConfigurationExtensions {
    /// <summary>
    /// Flattens an <see cref="IConfiguration"/> into a flat dictionary using ':' as the key separator.
    /// Section paths are made relative so the result reflects the section's own keys.
    /// </summary>
    public static Dictionary<string, string> ToFlatDictionary(this IConfiguration configuration) =>
        configuration.AsEnumerable(makePathsRelative: true)
            .Where(kvp => kvp.Value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

    /// <summary>
    /// Parses a JSON string and flattens it into a dictionary using ':' as the key separator.
    /// Nested objects produce colon-separated keys; arrays use numeric indices.
    /// </summary>
    public static Dictionary<string, string> ToFlatDictionary(this string json) {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build().ToFlatDictionary();
    }

    /// <summary>
    /// Flattens a hierarchical Protobuf <see cref="Struct"/> into a flat dictionary using ':' as the key separator.
    /// Useful for consuming hierarchical configuration returned by the server.
    /// </summary>
    public static Dictionary<string, string> FlattenStruct(this Struct source) {
        var dictionary = new Dictionary<string, string>();
        FlattenStruct(source, string.Empty, dictionary);
        return dictionary;
    }

    static void FlattenStruct(Struct source, string prefix, Dictionary<string, string> dictionary) {
        foreach (var field in source.Fields) {
            var key = string.IsNullOrEmpty(prefix) ? field.Key : $"{prefix}:{field.Key}";
            FlattenValue(field.Value, key, dictionary);
        }
    }

    static void FlattenValue(Value value, string key, Dictionary<string, string> dictionary) {
        switch (value.KindCase) {
            case Value.KindOneofCase.NullValue:
                dictionary[key] = "";
                break;

            case Value.KindOneofCase.NumberValue:
                dictionary[key] = value.NumberValue.ToString(CultureInfo.InvariantCulture);
                break;

            case Value.KindOneofCase.StringValue:
                dictionary[key] = value.StringValue;
                break;

            case Value.KindOneofCase.BoolValue:
                dictionary[key] = value.BoolValue.ToString();
                break;

            case Value.KindOneofCase.StructValue:
                FlattenStruct(value.StructValue, key, dictionary);
                break;

            case Value.KindOneofCase.ListValue:
                for (var i = 0; i < value.ListValue.Values.Count; i++)
                    FlattenValue(value.ListValue.Values[i], $"{key}:{i}", dictionary);
                break;
        }
    }
}
