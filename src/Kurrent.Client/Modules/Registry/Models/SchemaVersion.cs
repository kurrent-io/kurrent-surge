using Kurrent.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Client.Registry;

public readonly record struct SchemaVersion(
    SchemaVersionId VersionId,
    int VersionNumber,
    string SchemaDefinition,
    SchemaDataFormat DataFormat,
    DateTimeOffset RegisteredAt
);
