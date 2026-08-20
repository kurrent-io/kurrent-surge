using Avro.Specific;

namespace Kurrent.Surge.Codecs.Avro;

static class AvroExtensions {
    public static ISpecificRecord EnsureValueIsAvroRecord(this object? value) =>
        value as ISpecificRecord ?? throw new InvalidOperationException($"Value of type {value!.GetType().Name} is not an Avro ISpecificRecord. Generate it from an .avsc schema with avrogen.");
}
