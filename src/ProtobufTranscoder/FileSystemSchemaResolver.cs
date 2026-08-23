namespace ProtobufTranscoder;

public sealed class FileSystemSchemaResolver(string rootPath) : IProtoSchemaResolver
{
    public string? Resolve(string importPath)
    {
        var fullPath = Path.Combine(rootPath, importPath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }
}
