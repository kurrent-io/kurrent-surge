// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

public partial class RegistryClient {
    #region ->> GetSchema <<-

    /// <summary>
    /// Retrieves the schema details for the specified schema name from the registry.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema to retrieve.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Schema"/> containing the retrieved schema details.</returns>
    public async ValueTask<Schema> GetSchema(SchemaName schemaName, CancellationToken ct = default) {
        if (schemaName.IsUnset)
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        var request = new Contracts.GetSchemaRequest { SchemaName = schemaName };

        try {
            var response = await ServiceClient
                .GetSchemaAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Schema.ToSchema();
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException(schemaName)));
        }
    }

    #endregion

    #region ->> GetSchemaVersion <<-

    /// <summary>
    /// Retrieves the latest version of a schema from the registry.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema whose latest version is being retrieved.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersion"/> containing the latest schema version details.</returns>
    public ValueTask<SchemaVersion> GetSchemaVersion(SchemaName schemaName, CancellationToken ct = default) =>
        GetSchemaVersionCore(schemaName, versionNumber: null, ct);

    /// <summary>
    /// Retrieves a specific version of a schema from the registry.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema whose version is being retrieved.</param>
    /// <param name="versionNumber">Version number of the schema to retrieve.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersion"/> containing the schema version details.</returns>
    public ValueTask<SchemaVersion> GetSchemaVersion(
        SchemaName schemaName, int versionNumber, CancellationToken ct = default
    ) => GetSchemaVersionCore(schemaName, versionNumber, ct);

    async ValueTask<SchemaVersion> GetSchemaVersionCore(
        SchemaName schemaName, int? versionNumber, CancellationToken ct
    ) {
        if (schemaName.IsUnset)
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        if (versionNumber is { } v && v <= 0)
            throw new ArgumentOutOfRangeException(nameof(versionNumber), v, "Version number must be greater than zero.");

        var request = new Contracts.GetSchemaVersionRequest { SchemaName = schemaName };

        if (versionNumber.HasValue)
            request.VersionNumber = versionNumber.Value;

        try {
            var response = await ServiceClient
                .GetSchemaVersionAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Version.ToSchemaVersion();
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException(schemaName)));
        }
    }

    #endregion

    #region ->> GetSchemaVersionById <<-

    /// <summary>
    /// Retrieves the schema version details for the specified schema version id from the registry.
    /// </summary>
    /// <param name="schemaVersionId">Unique identifier of the schema version to retrieve.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersion"/> containing the requested schema version details.</returns>
    public async ValueTask<SchemaVersion> GetSchemaVersionById(
        SchemaVersionId schemaVersionId, CancellationToken ct = default
    ) {
        if (schemaVersionId.IsUnset)
            throw new ArgumentException("Schema version id cannot be empty.", nameof(schemaVersionId));

        var request = new Contracts.GetSchemaVersionByIdRequest { SchemaVersionId = schemaVersionId };

        try {
            var response = await ServiceClient
                .GetSchemaVersionByIdAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Version.ToSchemaVersion();
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException($"Schema version '{schemaVersionId}' was not found.")),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException($"Schema for version '{schemaVersionId}' has been deleted.")));
        }
    }

    #endregion

    #region ->> CheckSchemaCompatibility <<-

    /// <summary>
    /// Checks the compatibility of a schema definition against a given schema identifier and data format.
    /// </summary>
    /// <param name="identifier">Schema identifier, which can either be a schema name or a schema version id.</param>
    /// <param name="schemaDefinition">Schema definition to check for compatibility in string format.</param>
    /// <param name="dataFormat">Format of the schema, such as Json, Protobuf, Avro, or Bytes.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionId"/> on success, indicating the compatible schema version.</returns>
    public async ValueTask<SchemaVersionId> CheckSchemaCompatibility(
        SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat,
        CancellationToken ct = default
    ) {
        if (identifier.IsUnset)
            throw new ArgumentException("Schema identifier cannot be empty.", nameof(identifier));

        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDefinition);

        var request = new Contracts.CheckSchemaCompatibilityRequest {
            Definition = ByteString.CopyFromUtf8(schemaDefinition),
            DataFormat = (Contracts.SchemaDataFormat)dataFormat
        };

        identifier.Switch(
            sn => request.SchemaName = sn,
            sv => request.SchemaVersionId = sv
        );

        try {
            var response = await ServiceClient
                .CheckSchemaCompatibilityAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            if (response.ResultCase is Contracts.CheckSchemaCompatibilityResponse.ResultOneofCase.Success)
                return SchemaVersionId.From(response.Success.SchemaVersionId);

            throw new SchemaIncompatibleException(SchemaCompatibilityErrors.FromProto(response.Failure.Errors));
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException($"Schema '{identifier}' was not found.")),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException($"Schema '{identifier}' has been deleted.")));
        }
    }

    #endregion

    #region ->> ListSchemas <<-

    /// <summary>
    /// Lists schemas in the registry, filtered by name prefix and tags.
    /// </summary>
    /// <param name="schemaNamePrefix">Prefix to filter schemas by name. Only schemas whose names start with this prefix are returned. Pass null for no prefix filter.</param>
    /// <param name="tags">Key-value pairs to filter schemas by tags. Only schemas matching all specified tags are returned. Pass null for no tag filter.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>An <see cref="IReadOnlyList{Schema}"/> containing the matching schemas.</returns>
    public async ValueTask<IReadOnlyList<Schema>> ListSchemas(
        string? schemaNamePrefix, IReadOnlyDictionary<string, string>? tags, CancellationToken ct = default
    ) {
        var request = new Contracts.ListSchemasRequest();

        if (schemaNamePrefix is not null)
            request.SchemaNamePrefix = schemaNamePrefix;

        if (tags is not null)
            foreach (var (key, value) in tags)
                request.SchemaTags.Add(key, value);

        try {
            var response = await ServiceClient
                .ListSchemasAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemas();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    #endregion
}
