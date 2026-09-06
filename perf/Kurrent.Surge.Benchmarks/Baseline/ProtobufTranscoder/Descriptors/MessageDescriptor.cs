namespace ProtobufTranscoder.Descriptors;

public sealed class MessageDescriptor
{
    public string FullName { get; }
    public IReadOnlyList<FieldDescriptor> Fields { get; }
    public IReadOnlyList<OneofDescriptor> Oneofs { get; }
    public IReadOnlyList<MessageDescriptor> NestedTypes { get; }
    public IReadOnlyList<EnumDescriptor> EnumTypes { get; }
    public IReadOnlyList<ExtensionRange> ExtensionRanges { get; }

    /// <summary>True when this message is a synthetic map entry type.</summary>
    public bool IsMapEntry { get; internal set; }

    private readonly Dictionary<int, FieldDescriptor> _byNumber;

    // Set by ProtoDefinitions to enable extension lookup
    internal ProtoDefinitions? Pool { get; set; }

    internal MessageDescriptor(
        string fullName,
        IReadOnlyList<FieldDescriptor> fields,
        IReadOnlyList<OneofDescriptor> oneofs,
        IReadOnlyList<MessageDescriptor> nestedTypes,
        IReadOnlyList<EnumDescriptor> enumTypes,
        IReadOnlyList<ExtensionRange>? extensionRanges = null)
    {
        FullName = fullName;
        Fields = fields;
        Oneofs = oneofs;
        NestedTypes = nestedTypes;
        EnumTypes = enumTypes;
        ExtensionRanges = extensionRanges ?? [];
        _byNumber = new Dictionary<int, FieldDescriptor>(fields.Count);
        foreach (var f in fields)
            _byNumber[f.Number] = f;
    }

    public FieldDescriptor? FindFieldByNumber(int number)
    {
        if (_byNumber.TryGetValue(number, out var field))
            return field;

        // Check extension registry if the number falls within an extension range
        if (Pool is not null && IsInExtensionRange(number))
            return Pool.FindExtension(FullName, number);

        return null;
    }

    private bool IsInExtensionRange(int number)
    {
        foreach (var range in ExtensionRanges)
        {
            if (number >= range.Start && number <= range.End)
                return true;
        }
        return false;
    }

    internal void RegisterField(FieldDescriptor field)
    {
        _byNumber[field.Number] = field;
    }
}
