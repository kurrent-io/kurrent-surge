using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Registry;

public readonly record struct SchemaVersion(
    SchemaVersionId VersionId,
    int VersionNumber,
    string SchemaDefinition,
    SchemaDataFormat DataFormat,
    DateTimeOffset RegisteredAt
);
