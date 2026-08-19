using System.Collections.Concurrent;
using Avro.Specific;
using AvroSchema = global::Avro.Schema;

namespace Kurrent.Surge.Codecs.Avro;

static class AvroExtensions {
    public static ISpecificRecord EnsureValueIsAvroRecord(this object? value) =>
        value as ISpecificRecord ?? throw new InvalidOperationException($"Value of type {value!.GetType().Name} is not an Avro ISpecificRecord. Generate it from an .avsc schema with avrogen.");

    public static Type EnsureTypeIsAvroRecord(this Type recordType) =>
        !typeof(ISpecificRecord).IsAssignableFrom(recordType)
            ? throw new InvalidCastException($"Type {recordType.Name} is not an Avro ISpecificRecord. Generate it from an .avsc schema with avrogen.")
            : recordType;
}

/// <summary>
/// Caches the <see cref="AvroSchema"/> and the reflection-resolved reader/writer pair for each
/// <see cref="ISpecificRecord"/> type, keyed by the runtime <see cref="Type"/>. The schema comes
/// straight off an instance of the avrogen-generated type — never derived, never parsed by hand.
/// </summary>
class AvroCodecs {
    public static AvroCodecs System { get; } = new();

    ConcurrentDictionary<Type, (AvroSchema Schema, SpecificDatumWriter<ISpecificRecord> Writer, SpecificDatumReader<ISpecificRecord> Reader)> Types { get; } = new();

    public (AvroSchema Schema, SpecificDatumWriter<ISpecificRecord> Writer, SpecificDatumReader<ISpecificRecord> Reader) GetCodec(Type recordType) =>
        Types.GetOrAdd(recordType, CreateCodec);

    static (AvroSchema, SpecificDatumWriter<ISpecificRecord>, SpecificDatumReader<ISpecificRecord>) CreateCodec(Type recordType) {
        var probe  = (ISpecificRecord)Activator.CreateInstance(recordType.EnsureTypeIsAvroRecord())!;
        var schema = probe.Schema;

        return (schema, new(schema), new(schema, schema));
    }
}
