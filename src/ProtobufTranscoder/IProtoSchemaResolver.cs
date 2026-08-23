namespace ProtobufTranscoder;

public interface IProtoSchemaResolver
{
    string? Resolve(string importPath);
}
