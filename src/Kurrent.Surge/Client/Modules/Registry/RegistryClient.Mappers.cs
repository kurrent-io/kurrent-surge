using Google.Protobuf.Collections;
using Kurrent.Surge.Schema;
using NJsonSchema;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Surge.Client.Registry;

static class RegistryClientMapper {
    // The proto-facing halves of SchemaCompatibilityError(s): the models moved to the contract
    // package with the schema subsystem, but these mappers speak proto-generated types compiled
    // in this assembly, so they stay behind.
    internal static SchemaCompatibilityError ToSchemaCompatibilityError(this Contracts.SchemaCompatibilityError error) {
        ArgumentNullException.ThrowIfNull(error);

        return new() {
            Kind         = (SchemaCompatibilityErrorKind)error.Kind,
            Details      = error.Details,
            PropertyPath = error.PropertyPath,
            OriginalType = Enum.TryParse<JsonObjectType>(error.OriginalType, out var originalType) ? originalType : JsonObjectType.None,
            NewType      = Enum.TryParse<JsonObjectType>(error.NewType, out var newType) ? newType : JsonObjectType.None
        };
    }

    internal static SchemaCompatibilityErrors ToSchemaCompatibilityErrors(this RepeatedField<Contracts.SchemaCompatibilityError> errors) =>
        SchemaCompatibilityErrors.From(errors.Select(e => e.ToSchemaCompatibilityError()).ToArray());

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
