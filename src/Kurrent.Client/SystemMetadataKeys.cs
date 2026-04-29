namespace Kurrent.Client;

static class SystemMetadataKeys {
    const string SystemPrefix = "$";

    public const string SchemaName   = $"{SystemPrefix}schema.name";
    public const string SchemaFormat = $"{SystemPrefix}schema.format"; // --> schema.format
    public const string SchemaId     = $"{SystemPrefix}schema.id";     // --> schema.id
    public const string SchemaUrn    = $"{SystemPrefix}schema.urn";    // schema name + format + version id

    public const string Timestamp = $"{SystemPrefix}record.timestamp";

    // // /// <summary>
    // // /// possibly used internally and then removed right after.
    // // /// </summary>
    // // internal const string Stream = $"{SystemPrefix}stream.name";
    //
    // public static bool IsSystemMetadataKey(ReadOnlySpan<char> key) => key.StartsWith(SystemPrefix, StringComparison.Ordinal);
    //
    // public static bool IsKnownSystemMetadataKey(ReadOnlySpan<char> key) =>
    //     // key.Equals(Stream, StringComparison.Ordinal) ||
    //     key.Equals(SchemaName, StringComparison.Ordinal) ||
    //     key.Equals(SchemaFormat, StringComparison.Ordinal) ||
    //     key.Equals(SchemaId, StringComparison.Ordinal) ||
    //     key.Equals(SchemaUrn, StringComparison.Ordinal);
}
