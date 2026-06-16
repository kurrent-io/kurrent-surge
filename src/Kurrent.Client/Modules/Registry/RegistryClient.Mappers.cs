using Kurrent.Client.Streams;
using Kurrent.Schema;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

static class RegistryClientMapper {
    internal static SchemaVersionDescriptor ToSchemaVersionDescriptor(this Contracts.CreateSchemaResponse response) =>
        new(Guid.Parse(response.SchemaVersionId), response.VersionNumber);

    internal static Schema ToSchema(this Contracts.Schema schema) =>
        new(
            SchemaName.From(schema.SchemaName),
            schema.Details.ToSchemaDetails(),
            schema.LatestSchemaVersion,
            schema.CreatedAt.ToDateTimeOffset(),
            schema.UpdatedAt.ToDateTimeOffset()
        );

    internal static SchemaVersion ToSchemaVersion(this Contracts.SchemaVersion version) =>
        new(
            SchemaVersionId.From(version.SchemaVersionId),
            version.VersionNumber,
            version.SchemaDefinition.ToStringUtf8(),
            (SchemaDataFormat)version.DataFormat,
            version.RegisteredAt.ToDateTimeOffset()
        );

    internal static SchemaVersionDescriptor ToSchemaVersionDescriptor(this Contracts.RegisterSchemaVersionResponse response) =>
        new(Guid.Parse(response.SchemaVersionId), response.VersionNumber);

    internal static IReadOnlyList<Schema> ToSchemas(this Contracts.ListSchemasResponse response) =>
        response.Schemas.Select(s => s.ToSchema()).ToList();

    static SchemaDetails ToSchemaDetails(this Contracts.SchemaDetails details) =>
        new(
            (SchemaDataFormat)details.DataFormat,
            (CompatibilityMode)details.Compatibility,
            details.HasDescription ? details.Description : "",
            details.Tags
        );
}
