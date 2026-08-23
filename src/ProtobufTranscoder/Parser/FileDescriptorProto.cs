using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Parser;

/// <summary>Raw parse output for a .proto file, before cross-reference resolution.</summary>
internal sealed class FileDescriptorProto
{
    public string Name { get; init; } = "";
    public string Syntax { get; set; } = "proto3";
    public string? Edition { get; set; }
    public string Package { get; set; } = "";
    public List<string> Imports { get; } = [];
    public List<MessageDescriptorProto> Messages { get; } = [];
    public List<EnumDescriptorProto> Enums { get; } = [];
    public List<ExtendBlockProto> ExtendBlocks { get; } = [];
    public FeatureSetOverrides? Features { get; set; }
}

internal sealed class MessageDescriptorProto
{
    public string Name { get; init; } = "";
    public List<FieldDescriptorProto> Fields { get; } = [];
    public List<MessageDescriptorProto> NestedMessages { get; } = [];
    public List<EnumDescriptorProto> Enums { get; } = [];
    public List<string> Oneofs { get; } = [];
    public bool IsMapEntry { get; set; }
    public List<ExtensionRangeProto> ExtensionRanges { get; } = [];
    public List<ExtendBlockProto> ExtendBlocks { get; } = [];
    public FeatureSetOverrides? Features { get; set; }
}

internal sealed class FieldDescriptorProto
{
    public string Name { get; init; } = "";
    public string? JsonName { get; init; }
    public int Number { get; init; }
    public FieldType Type { get; init; }
    public FieldLabel Label { get; init; }
    public string TypeName { get; init; } = "";
    public int OneofIndex { get; init; } = -1;
    public bool IsPacked { get; init; }
    public bool IsMap { get; init; }
    public string? DefaultValue { get; init; }
    public bool IsGroupEncoded { get; init; }
    public FeatureSetOverrides? Features { get; set; }
}

internal sealed class EnumDescriptorProto
{
    public string Name { get; init; } = "";
    public List<EnumValueProto> Values { get; } = [];
    public FeatureSetOverrides? Features { get; set; }
}

internal sealed class EnumValueProto
{
    public string Name { get; init; } = "";
    public int Number { get; init; }
}

internal sealed class ExtensionRangeProto
{
    public int Start { get; init; }
    public int End { get; init; }
}

/// <summary>
/// Represents an extend block: extend SomeMessage { fields... }
/// </summary>
internal sealed class ExtendBlockProto
{
    public string ExtendeeTypeName { get; init; } = "";
    public List<FieldDescriptorProto> Fields { get; } = [];
}
