using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;

namespace Kurrent.Client.Streams;

partial class StreamsProtocolMapper(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy registryPolicy) {
    ISchemaSerializerProvider SerializerProvider { get; } = serializerProvider;
    SchemaRegistryPolicy      RegistryPolicy     { get; } = registryPolicy;
}
