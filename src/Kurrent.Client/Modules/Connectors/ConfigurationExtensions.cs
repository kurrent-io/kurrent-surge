using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Connectors;

/// <summary>
/// Extension methods for converting between flat dictionaries, IConfiguration, and Protobuf Struct.
/// </summary>
public static class ConfigurationExtensions {
    /// <summary>
    /// Converts a flat dictionary to a hierarchical Protobuf Struct.
    /// Keys using ':' as separator are converted to nested objects.
    /// </summary>
    public static Struct ToProtobufStruct(this IReadOnlyDictionary<string, string?> configuration) =>
        new ConfigurationBuilder().AddInMemoryCollection(configuration).Build().ToProtobufStruct();

    /// <summary>
    /// Converts an IConfiguration to a Protobuf Struct, preserving the hierarchical structure.
    /// </summary>
    public static Struct ToProtobufStruct(this IConfiguration configuration) {
        var protoStruct = new Struct();

        foreach (var child in configuration.GetChildren()) {
            var value = ConvertSectionToValue(child);

            if (value != null)
                protoStruct.Fields[child.Key] = value;
        }

        return protoStruct;

        static Value? ConvertSectionToValue(IConfigurationSection section) {
            var children = section.GetChildren().ToList();

            if (children.Count == 0)
                // parse leaf node, otherwise return null to omit empty keys
                return !string.IsNullOrEmpty(section.Value)
                    ? ParseValue(section.Value)
                    : null;

            // Determine if the section represents an array
            if (children.All(c => long.TryParse(c.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) {
                return Value
                    .ForList(children.OrderBy(c => long.Parse(c.Key, NumberStyles.Integer, CultureInfo.InvariantCulture))
                        .Select(ConvertSectionToValue)
                        .OfType<Value>().ToArray());
            }

            // Create a Struct for nested objects
            var structValue = new Struct();

            foreach (var child in children) {
                var childValue = ConvertSectionToValue(child);

                if (childValue is not null)
                    structValue.Fields[child.Key] = childValue;
            }

            return Value.ForStruct(structValue);
        }
    }

    /// <summary>
    /// Flattens a hierarchical Protobuf Struct into a flat dictionary using ':' as key separator.
    /// </summary>
    public static Dictionary<string, string?> FlattenStruct(this Struct source) {
        var dictionary = new Dictionary<string, string?>();
        FlattenStruct(source, string.Empty, dictionary);
        return dictionary;
    }

    /// <summary>
    /// Flattens a hierarchical Protobuf Struct into a flat dictionary with a key prefix.
    /// </summary>
    public static void FlattenStruct(Struct source, string prefix, Dictionary<string, string?> dictionary) {
        foreach (var field in source.Fields) {
            var key = string.IsNullOrEmpty(prefix) ? field.Key : $"{prefix}:{field.Key}";
            FlattenValue(field.Value, key, dictionary);
        }
    }

    static void FlattenValue(Value value, string key, Dictionary<string, string?> dictionary) {
        switch (value.KindCase) {
            case Value.KindOneofCase.NullValue:
                dictionary[key] = null;
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
                for (var i = 0; i < value.ListValue.Values.Count; i++) {
                    var itemKey = $"{key}:{i}";
                    FlattenValue(value.ListValue.Values[i], itemKey, dictionary);
                }
                break;
        }
    }

    internal static Value ParseValue(string value) {
        if (bool.TryParse(value, out var boolValue))
            return Value.ForBool(boolValue);

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return Value.ForNumber(intValue);

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return Value.ForNumber(longValue);

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return Value.ForNumber(doubleValue);

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _))
            return Value.ForString(value);

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateValue))
            return Value.ForString(dateValue.ToString("O", CultureInfo.InvariantCulture));

        if (Guid.TryParse(value, out var guidValue))
            return Value.ForString(guidValue.ToString());

        return Value.ForString(string.IsNullOrEmpty(value) ? string.Empty : value);
    }
}
