// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Schema;
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
    /// <param name="request">The compatibility check to perform. Use <see cref="CheckSchemaCompatibilityRequest.For"/> to build one fluently.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionId"/> on success, indicating the compatible schema version.</returns>
    public async ValueTask<SchemaVersionId> CheckSchemaCompatibility(
        CheckSchemaCompatibilityRequest request, CancellationToken ct = default
    ) {
        request.EnsureValid();

        var rpc = new Contracts.CheckSchemaCompatibilityRequest {
            Definition = ByteString.CopyFromUtf8(request.SchemaDefinition),
            DataFormat = (Contracts.SchemaDataFormat)request.DataFormat
        };

        request.Identifier.Switch(
            sn => rpc.SchemaName = sn,
            sv => rpc.SchemaVersionId = sv
        );

        try {
            var response = await ServiceClient
                .CheckSchemaCompatibilityAsync(rpc, cancellationToken: ct)
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
                StatusCode.NotFound.MapTo(new SchemaNotFoundException($"Schema '{request.Identifier}' was not found.")),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException($"Schema '{request.Identifier}' has been deleted.")));
        }
    }

    #endregion

    #region ->> ListSchemas <<-

    /// <summary>
    /// Lists schemas in the registry, optionally filtered by name prefix and tags.
    /// </summary>
    /// <param name="request">The filter to apply. Use <see cref="ListSchemasRequest.New"/> to build one, or pass an empty request for no filtering.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>An <see cref="IReadOnlyList{Schema}"/> containing the matching schemas.</returns>
    public async ValueTask<IReadOnlyList<Schema>> ListSchemas(
        ListSchemasRequest request, CancellationToken ct = default
    ) {
        request.EnsureValid();

        var rpc = new Contracts.ListSchemasRequest();

        if (!string.IsNullOrEmpty(request.SchemaNamePrefix))
            rpc.SchemaNamePrefix = request.SchemaNamePrefix;

        foreach (var (key, value) in request.Tags)
            rpc.SchemaTags.Add(key, value);

        try {
            var response = await ServiceClient
                .ListSchemasAsync(rpc, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemas();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    #endregion
}
