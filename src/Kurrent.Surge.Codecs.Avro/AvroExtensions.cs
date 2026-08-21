using Avro.Specific;

namespace Kurrent.Surge.Codecs.Avro;

static class AvroExtensions {
    public static ISpecificRecord EnsureValueIsAvroRecord(this object? value) =>
        value as ISpecificRecord ?? throw new InvalidOperationException($"Value of type {value?.GetType().Name ?? "null"} is not an Avro ISpecificRecord. Add its .avsc schema as an AdditionalFile so the codec generator emits the type.");
}
