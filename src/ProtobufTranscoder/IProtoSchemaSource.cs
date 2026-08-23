namespace ProtobufTranscoder;

public interface IProtoSchemaSource
{
    ProtoSchemaInfo Resolve(string schemaId);
}

public record ProtoSchemaInfo(
    string MessageTypeName,
    ProtoSchema[] Schemas,
    string Version);
