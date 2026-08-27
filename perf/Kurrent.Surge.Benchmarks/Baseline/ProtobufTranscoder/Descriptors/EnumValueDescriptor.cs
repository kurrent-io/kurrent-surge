namespace ProtobufTranscoder.Descriptors;

public sealed class EnumValueDescriptor
{
    public string Name { get; }
    public int Number { get; }

    internal EnumValueDescriptor(string name, int number)
    {
        Name = name;
        Number = number;
    }
}
