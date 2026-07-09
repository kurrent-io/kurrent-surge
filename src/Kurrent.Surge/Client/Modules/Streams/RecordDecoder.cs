using System.Text.Json.Nodes;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Schema;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kurrent.Surge.Client.Streams;

// The concrete decoder lives in the client, not the contract: it depends on the schema
// serialization layer (ISchemaSerializerProvider, SchemaSerializationContext) and the JSON
// serialization context (KurrentJsonContext, which pulls in NodaTime). Record stays a plain
// contract type in Kurrent.Surge.Core and reaches both through the IRecordDecoder seam.
sealed class RecordDecoder(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy? registryPolicy = null) : IRecordDecoder {
    SchemaRegistryPolicy RegistryPolicy { get; } = registryPolicy ?? SchemaRegistryPolicy.NoRequirements;

    public async ValueTask<DecodeOutcome> TryDecode(Record record, CancellationToken ct) {
        if (record.IsDecoded)
            return DecodeOutcome.Decoded;

        var context = new SchemaSerializationContext {
            Stream               = record.Stream,
            RecordSchema         = record.Schema,
            SchemaRegistryPolicy = RegistryPolicy,
            CancellationToken    = ct
        };

        var result = await serializerProvider
            .GetSerializer(record.Schema.DataFormat)
            .Deserialize(record.Data, context)
            .ConfigureAwait(false);

        switch (result) {
            case DeserializationResult.Deserialized deserialized:
                // The bytes pass-through "deserializes" to the raw memory itself — Data
                // already holds it, so there is nothing to materialize: decoding is not
                // applicable, which is exactly what Unavailable reports.
                if (deserialized.Value is ReadOnlyMemory<byte>)
                    return DecodeOutcome.Unavailable;

                record.Value     = deserialized.Value;
                record.ValueType = deserialized.Value.GetType();

                return DecodeOutcome.Decoded;

            case DeserializationResult.NoData:
                return DecodeOutcome.NoData;

            case DeserializationResult.UnknownType:
                return DecodeOutcome.UnknownType;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown deserialization result");
        }
    }

    public JsonNode? ProjectToJson(Record record) =>
        record.IsDecoded && record.Schema.DataFormat == SchemaDataFormat.Json
            ? JsonSerializer.SerializeToNode(record.Value, record.ValueType, KurrentJsonContext.Default)
            : null;
}
