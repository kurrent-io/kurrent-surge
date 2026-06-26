using Kurrent.Schema;

namespace Kurrent.Client.Registry;

public readonly record struct Schema(
    SchemaName SchemaName,
    SchemaDetails Details,
    int LatestSchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
