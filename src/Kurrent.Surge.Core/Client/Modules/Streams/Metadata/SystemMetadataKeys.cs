namespace Kurrent.Surge.Client;

static class SystemMetadataKeys {
    const string SystemPrefix = "$";

    public const string SchemaName   = $"{SystemPrefix}schema.name";
    public const string SchemaFormat = $"{SystemPrefix}schema.format"; 
    public const string SchemaId     = $"{SystemPrefix}schema.id";
    public const string SchemaUrn    = $"{SystemPrefix}schema.urn";    // schema name + format + version id

    public const string Timestamp = $"{SystemPrefix}record.timestamp";
}
