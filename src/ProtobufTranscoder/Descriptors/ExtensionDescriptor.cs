namespace ProtobufTranscoder.Descriptors;

public sealed class ExtensionDescriptor
{
    public FieldDescriptor Field { get; }
    public MessageDescriptor ExtendeeType { get; }

    internal ExtensionDescriptor(FieldDescriptor field, MessageDescriptor extendeeType)
    {
        Field = field;
        ExtendeeType = extendeeType;
    }
}

public readonly record struct ExtensionRange(int Start, int End);
