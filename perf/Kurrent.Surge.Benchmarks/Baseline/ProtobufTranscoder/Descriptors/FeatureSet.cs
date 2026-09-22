namespace ProtobufTranscoder.Descriptors;

sealed class FeatureSet
{
    public FieldPresence FieldPresence { get; init; }
    public EnumType EnumType { get; init; }
    public RepeatedFieldEncoding RepeatedFieldEncoding { get; init; }
    public MessageEncoding MessageEncoding { get; init; }
    public JsonFormat JsonFormat { get; init; }

    public static FeatureSet DefaultForProto2 { get; } = new()
    {
        FieldPresence = FieldPresence.Explicit,
        EnumType = EnumType.Closed,
        RepeatedFieldEncoding = RepeatedFieldEncoding.Expanded,
        MessageEncoding = MessageEncoding.LengthPrefixed,
        JsonFormat = JsonFormat.LegacyBestEffort,
    };

    public static FeatureSet DefaultForProto3 { get; } = new()
    {
        FieldPresence = FieldPresence.Implicit,
        EnumType = EnumType.Open,
        RepeatedFieldEncoding = RepeatedFieldEncoding.Packed,
        MessageEncoding = MessageEncoding.LengthPrefixed,
        JsonFormat = JsonFormat.Allow,
    };

    public static FeatureSet DefaultForEditions2024 { get; } = new()
    {
        FieldPresence = FieldPresence.Explicit,
        EnumType = EnumType.Open,
        RepeatedFieldEncoding = RepeatedFieldEncoding.Packed,
        MessageEncoding = MessageEncoding.LengthPrefixed,
        JsonFormat = JsonFormat.Allow,
    };

    public static FeatureSet GetDefault(string syntax) => syntax switch
    {
        "proto2" => DefaultForProto2,
        "proto3" => DefaultForProto3,
        "editions" => DefaultForEditions2024,
        _ => DefaultForProto3,
    };

    /// <summary>
    /// Merges child overrides onto a parent feature set.
    /// Non-null overrides replace parent values.
    /// </summary>
    public static FeatureSet Resolve(FeatureSet parent, FeatureSetOverrides? overrides)
    {
        if (overrides is null)
            return parent;

        return new FeatureSet
        {
            FieldPresence = overrides.FieldPresence ?? parent.FieldPresence,
            EnumType = overrides.EnumType ?? parent.EnumType,
            RepeatedFieldEncoding = overrides.RepeatedFieldEncoding ?? parent.RepeatedFieldEncoding,
            MessageEncoding = overrides.MessageEncoding ?? parent.MessageEncoding,
            JsonFormat = overrides.JsonFormat ?? parent.JsonFormat,
        };
    }
}

enum EnumType { Open, Closed }

enum RepeatedFieldEncoding { Packed, Expanded }

enum MessageEncoding { LengthPrefixed, Delimited }

enum JsonFormat { Allow, LegacyBestEffort }

/// <summary>
/// Nullable overrides parsed from features blocks.
/// Null values mean "inherit from parent".
/// </summary>
sealed record FeatureSetOverrides
{
    public FieldPresence? FieldPresence { get; init; }
    public EnumType? EnumType { get; init; }
    public RepeatedFieldEncoding? RepeatedFieldEncoding { get; init; }
    public MessageEncoding? MessageEncoding { get; init; }
    public JsonFormat? JsonFormat { get; init; }
}
