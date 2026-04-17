#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

using System.Collections;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Client.Streams;

[PublicAPI]
static class ProtocolMapper {
    public static Metadata ToMetadata(this MapField<string, Value> source) {
        return source.Aggregate(new Metadata(), (meta, entry) => meta.With(entry.Key, MapToMetadataValue(entry.Value)));

        static object? MapToMetadataValue(Value source) =>
            source.KindCase switch {
                Value.KindOneofCase.NullValue   => null,
                Value.KindOneofCase.None        => null,
                Value.KindOneofCase.StringValue => source.StringValue,
                Value.KindOneofCase.BoolValue   => source.BoolValue,
                Value.KindOneofCase.NumberValue => source.NumberValue,
                Value.KindOneofCase.StructValue => source.StructValue.Fields.ToMetadata(),
                Value.KindOneofCase.ListValue   => source.ListValue.Values.Select(MapToMetadataValue)
            };
    }

    public static MapField<string, Value> ToProperties(this Metadata source) =>
        source.Aggregate(new MapField<string, Value>(), (seed, entry) => {
            seed.Add(entry.Key, ToValue(entry.Value));
            return seed;
        });

    /// <summary>
    /// This is tricky, because we need to map a lot of different types to a protobuf value
    /// The main challenge is to decide what to do with byte arrays. We can either treat them as strings (base64) or as lists of numbers.
    /// By default we treat them as strings, but if the flag is set to true, we convert them to lists of numbers.
    /// Need to figure this out properly even with numbers, because protobuf only supports double as number type.
    /// perhaps we can go lazy with a try/catch around map to value and if it fails we convert to string?
    /// </summary>
    static Value ToValue(object? source, bool treatByteArrayAsString = false) {
        if (IsNumber(source))
            return new() { NumberValue = Convert.ToDouble(source) };

        // somehow I need to check if the source is a collection or enumerable so I can map them to a list value
        if (source is IEnumerable and not string and not byte[] and not Metadata) {
            var values = ((IEnumerable)source).Cast<object?>().Select(val => ToValue(val, treatByteArrayAsString));
            return new() { ListValue = new() { Values = { values } } };
        }

        return source switch {
            null       => new() { NullValue   = NullValue.NullValue },
            string x   => new() { StringValue = x },
            bool x     => new() { BoolValue   = x },
            Metadata x => new() { StructValue = new() { Fields = { x.ToProperties() } } },

            ReadOnlyMemory<byte> x => new() { StringValue = Convert.ToBase64String(x.Span) },
            Memory<byte> x         => new() { StringValue = Convert.ToBase64String(x.Span) },
            ArraySegment<byte> x   => new() { StringValue = Convert.ToBase64String(x.Array ?? x.ToArray()) },
            byte[] x               => new() { StringValue = Convert.ToBase64String(x) },

            _ => new() { StringValue = source.ToString() }
        };

        static bool IsNumber(object? source) =>
            source is int or double or float or decimal or long or short or byte or sbyte or ushort or uint or ulong;
    }
}
