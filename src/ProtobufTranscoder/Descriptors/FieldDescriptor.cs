using System.Text;

namespace ProtobufTranscoder.Descriptors;

public sealed class FieldDescriptor {
    public string     Name     { get; }
    public string     JsonName { get; }
    public int        Number   { get; }
    public FieldType  Type     { get; internal set; }
    public FieldLabel Label    { get; }

    public bool               IsMap           { get; internal set; }
    public bool               IsPacked        { get; internal set; }
    public MessageDescriptor? MessageType     { get; internal set; }
    public EnumDescriptor?    EnumType        { get; internal set; }
    public OneofDescriptor?   ContainingOneof { get; internal set; }

    // Stage 2: presence, defaults, groups
    public FieldPresence Presence           { get; internal set; }
    public object?       DefaultValue       { get; internal set; }
    public bool          HasExplicitDefault { get; internal set; }
    public bool          IsGroupEncoded     { get; internal set; }

    public bool IsRepeated => Label == FieldLabel.Repeated;

    internal FieldDescriptor(string name, string jsonName, int number, FieldType type, FieldLabel label) {
        Name     = name;
        JsonName = jsonName;
        Number   = number;
        Type     = type;
        Label    = label;
    }

    /// <summary>
    /// Converts a proto field name (snake_case) to JSON name (lowerCamelCase).
    /// </summary>
    internal static string ToJsonName(string protoName) {
        var sb = new StringBuilder(protoName.Length);

        var capitalizeNext = false;

        foreach (var c in protoName) {
            if (c == '_') {
                capitalizeNext = true;
            } else if (capitalizeNext) {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            } else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the zero-value default for a given field type.
    /// </summary>
    internal static object GetZeroDefault(FieldType type, EnumDescriptor? enumType) =>
        type switch {
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => 0,
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => 0L,
            FieldType.UInt32 or FieldType.Fixed32 => 0u,
            FieldType.UInt64 or FieldType.Fixed64 => 0uL,
            FieldType.Float => 0.0f,
            FieldType.Double => 0.0,
            FieldType.Bool => false,
            FieldType.String => "",
            FieldType.Bytes => Array.Empty<byte>(),
            FieldType.Enum => enumType?.Values.Count > 0 ? (object)enumType.Values[0] : 0,
            _ => null!,
        };
}
