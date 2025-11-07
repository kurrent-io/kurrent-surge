#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

using System.Collections;
using System.Numerics;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Client.Streams;

[PublicAPI]
static class RecordPropertiesMapper {
    public static Metadata MapToMetadata(this MapField<string, Value> source) =>
        source.Aggregate(
            new Metadata(),
            (seed, entry) => seed.With(entry.Key, MapToMetadataValue(entry.Value)));

    public static object? MapToMetadataValue(this Value source) {
        return source.KindCase switch {
            Value.KindOneofCase.NullValue   => null,
            Value.KindOneofCase.None        => null,
            Value.KindOneofCase.StringValue => source.StringValue,
            Value.KindOneofCase.BoolValue   => source.BoolValue,
            Value.KindOneofCase.NumberValue => source.NumberValue,
            Value.KindOneofCase.StructValue => source.StructValue.Fields.MapToMetadata(),
            Value.KindOneofCase.ListValue   => source.ListValue.Values.Select(MapToMetadataValue)
        };
    }

    public static MapField<string, Value> MapToMapField(this Metadata source) =>
        source.Aggregate(new MapField<string, Value>(), (seed, entry) => {
            seed.Add(entry.Key, MapToValue(entry.Value));
            return seed;
        });

    public record ValueMappingOptions(bool ConvertBytes = false);

    /// <summary>
    /// This is tricky, because we need to map a lot of different types to a protobuf value
    /// The main challenge is to decide what to do with byte arrays. We can either treat them as strings (base64) or as lists of numbers.
    /// By default we treat them as strings, but if the flag is set to true, we convert them to lists of numbers.
    /// Need to figure this out properly even with numbers, because protobuf only supports double as number type.
    /// perhaps we can go lazy with a try/catch around map to value and if it fails we convert to string?
    /// </summary>
    public static Value MapToValue(this object? source, bool treatByteArrayAsString = false) {
        if (source.IsNumber())
            return new Value { NumberValue = Convert.ToDouble(source) };

        // somehow I need to check if the source is somesort of collection or enumerableso I can map them to a list value
        if (source is IEnumerable and not string and not byte[] and not Metadata) {
            var values = ((IEnumerable)source).Cast<object?>()
                .Select(val => MapToValue(val, treatByteArrayAsString));

            return new Value { ListValue = new ListValue { Values = { values } } };
        }

        return source switch {
            null       => new Value { NullValue   = NullValue.NullValue },
            string x   => new Value { StringValue = x },
            bool x     => new Value { BoolValue   = x },
            // Metadata x => new Value { StructValue = new Struct { Fields = { x.MapToMapField() } } },

            // ReadOnlyMemory<byte> x => new Value { StringValue = Convert.ToBase64String(x.Span) },
            // Memory<byte> x         => new Value { StringValue = Convert.ToBase64String(x.Span) },
            // ArraySegment<byte> x   => new Value { StringValue = Convert.ToBase64String(x.Array ?? x.ToArray()) },
            // byte[] x               => new Value { StringValue = Convert.ToBase64String(x) },

            _ => new Value { StringValue = source.ToString() }
        };
    }

    static bool IsNumber(this object? obj) =>
        obj is int or double or float or decimal or long or short or byte or sbyte or ushort or uint or ulong;

    //
    // static bool TryConvertToDouble(this object? obj, out double number) {
    //     if (obj is double itIs) {
    //         number = itIs;
    //         return true;
    //     }
    //
    //     try {
    //         number = Convert.ToDouble(obj);
    //         return true;
    //     }
    //     catch (Exception) {
    //         number = double.NaN;
    //         return false;
    //     }
    // }
}
