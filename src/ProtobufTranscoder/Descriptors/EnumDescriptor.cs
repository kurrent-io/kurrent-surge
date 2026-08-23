namespace ProtobufTranscoder.Descriptors;

public sealed class EnumDescriptor
{
    public string FullName { get; }
    public IReadOnlyList<EnumValueDescriptor> Values { get; }
    public bool IsClosed { get; internal set; }

    private readonly Dictionary<int, EnumValueDescriptor> _byNumber;

    internal EnumDescriptor(string fullName, IReadOnlyList<EnumValueDescriptor> values)
    {
        FullName = fullName;
        Values = values;
        _byNumber = new Dictionary<int, EnumValueDescriptor>(values.Count);
        foreach (var v in values)
            _byNumber.TryAdd(v.Number, v);
    }

    public EnumValueDescriptor? FindValueByNumber(int number) =>
        _byNumber.GetValueOrDefault(number);
}
