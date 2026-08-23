namespace ProtobufTranscoder.Descriptors;

public sealed class OneofDescriptor
{
    public string Name { get; }
    public IReadOnlyList<FieldDescriptor> Fields => _fields;

    private readonly List<FieldDescriptor> _fields = [];

    internal OneofDescriptor(string name)
    {
        Name = name;
    }

    internal void AddField(FieldDescriptor field) => _fields.Add(field);
}
