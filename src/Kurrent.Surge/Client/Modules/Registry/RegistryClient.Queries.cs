// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Schema;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Surge.Client.Registry;

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
                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException($"Schema version '{schemaVersionId}' was not found.")),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException($"Schema for version '{schemaVersionId}' has been deleted.")));
        }
    }

    #endregion

    #region ->> CheckSchemaCompatibility <<-

    /// <summary>
    /// Checks the compatibility of a schema definition against a given schema identifier, configured
    /// inline from a fresh <see cref="CheckSchemaCompatibilityOptions"/>.
    /// </summary>
    /// <param name="identifier">Schema identifier (a schema name or a schema version id) to check against.</param>
    /// <param name="configure">Configures the compatibility check.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionId"/> on success, indicating the compatible schema version.</returns>
    public ValueTask<SchemaVersionId> CheckSchemaCompatibility(
        SchemaIdentifier identifier, Action<CheckSchemaCompatibilityOptions>? configure = null, CancellationToken ct = default
    ) {
        var options = new CheckSchemaCompatibilityOptions { Identifier = identifier };
        configure?.Invoke(options);
        return CheckSchemaCompatibility(options, ct);
    }

    /// <summary>
    /// Checks the compatibility of a schema definition against a given schema identifier and data format.
    /// </summary>
    /// <param name="options">The compatibility check to perform.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionId"/> on success, indicating the compatible schema version.</returns>
    public async ValueTask<SchemaVersionId> CheckSchemaCompatibility(
        CheckSchemaCompatibilityOptions options, CancellationToken ct = default
    ) {
        options.EnsureValid();

        var rpc = new Contracts.CheckSchemaCompatibilityRequest {
            Definition = ByteString.CopyFromUtf8(options.SchemaDefinition),
            DataFormat = (Contracts.SchemaDataFormat)options.DataFormat
        };

        options.Identifier.Switch(
            sn => rpc.SchemaName = sn,
            sv => rpc.SchemaVersionId = sv
        );

        try {
            var response = await ServiceClient
                .CheckSchemaCompatibilityAsync(rpc, cancellationToken: ct)
                .ConfigureAwait(false);

            if (response.ResultCase is Contracts.CheckSchemaCompatibilityResponse.ResultOneofCase.Success)
                return SchemaVersionId.From(response.Success.SchemaVersionId);

            throw new SchemaIncompatibleException(response.Failure.Errors.ToSchemaCompatibilityErrors());
        }
        catch (Exception ex) {
            throw ex.ToException(
                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException($"Schema '{options.Identifier}' was not found.")),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException($"Schema '{options.Identifier}' has been deleted.")));
        }
    }

    #endregion

    #region ->> ListSchemas <<-

    /// <summary>
    /// Lists schemas in the registry, configured inline from a fresh <see cref="ListSchemasOptions"/>.
    /// </summary>
    /// <param name="configure">Configures the filter to apply.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>An <see cref="IReadOnlyList{Schema}"/> containing the matching schemas.</returns>
    public ValueTask<IReadOnlyList<Schema>> ListSchemas(
        Action<ListSchemasOptions>? configure = null, CancellationToken ct = default
    ) {
        var options = new ListSchemasOptions();
        configure?.Invoke(options);
        return ListSchemas(options, ct);
    }

    /// <summary>
    /// Lists schemas in the registry, optionally filtered by name prefix and tags.
    /// </summary>
    /// <param name="options">The filter to apply, or empty options for no filtering.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>An <see cref="IReadOnlyList{Schema}"/> containing the matching schemas.</returns>
    public async ValueTask<IReadOnlyList<Schema>> ListSchemas(
        ListSchemasOptions options, CancellationToken ct = default
    ) {
        options.EnsureValid();

        var rpc = new Contracts.ListSchemasRequest();

        if (!string.IsNullOrEmpty(options.SchemaNamePrefix))
            rpc.SchemaNamePrefix = options.SchemaNamePrefix;

        foreach (var (key, value) in options.Tags)
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
