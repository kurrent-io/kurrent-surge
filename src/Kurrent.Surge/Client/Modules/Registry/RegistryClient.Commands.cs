using Google.Protobuf;
using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Schema;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Surge.Client.Registry;

public partial class RegistryClient {
    #region ->> CreateSchema <<-

    /// <summary>
    /// Creates a new schema in the registry, configured inline from a fresh <see cref="CreateSchemaOptions"/>.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema to create.</param>
    /// <param name="configure">Configures the options for the schema.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionDescriptor"/> representing the newly created schema version.</returns>
    public ValueTask<SchemaVersionDescriptor> CreateSchema(
        SchemaName schemaName, Action<CreateSchemaOptions>? configure = null, CancellationToken ct = default
    ) {
        var options = new CreateSchemaOptions { SchemaName = schemaName };
        configure?.Invoke(options);
        return CreateSchema(options, ct);
    }

    /// <summary>
    /// Creates a new schema in the registry with the provided details.
    /// </summary>
    /// <param name="options">The schema details to register.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionDescriptor"/> representing the newly created schema version.</returns>
    public async ValueTask<SchemaVersionDescriptor> CreateSchema(
        CreateSchemaOptions options, CancellationToken ct = default
    ) {
        options.EnsureValid();

        var details = new Contracts.SchemaDetails {
            DataFormat    = (Contracts.SchemaDataFormat)options.DataFormat,
            Compatibility = (Contracts.CompatibilityMode)options.CompatibilityMode,
        };

        if (!string.IsNullOrEmpty(options.Description))
            details.Description = options.Description;

        foreach (var (key, value) in options.Tags)
            details.Tags.Add(key, value);

        var rpc = new Contracts.CreateSchemaRequest {
            SchemaName       = options.SchemaName,
            Details          = details,
            SchemaDefinition = ByteString.CopyFromUtf8(options.SchemaDefinition)
        };

        try {
            var response = await ServiceClient
                .CreateSchemaAsync(rpc, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            throw ex.ToException(
                // StatusCode fallbacks
                SchemaAlreadyExistsException.Match(options.SchemaName));
        }
    }

    #endregion

    #region ->> DeleteSchema <<-

    /// <summary>
    /// Deletes an existing schema in the registry identified by the specified name, and all its versions.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema to delete.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="Success"/> indicating the schema was deleted successfully.</returns>
    public async ValueTask<Success> DeleteSchema(SchemaName schemaName, CancellationToken ct = default) {
        if (schemaName.IsUnset)
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        var request = new Contracts.DeleteSchemaRequest { SchemaName = schemaName };

        try {
            await ServiceClient
                .DeleteSchemaAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ex.ToException(
                // StatusCode fallbacks
                SchemaNotFoundException.Match(schemaName),
                SchemaDeletedException.Match(schemaName));
        }
    }

    #endregion

    #region ->> RegisterSchemaVersion <<-

    /// <summary>
    /// Registers a new version of an existing schema in the registry.
    /// The server validates the schema definition against the schema's configured compatibility mode.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema to register a new version for.</param>
    /// <param name="schemaDefinition">Schema definition in string format for the new version.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionDescriptor"/> representing the newly registered schema version.</returns>
    public async ValueTask<SchemaVersionDescriptor> RegisterSchemaVersion(
        SchemaName schemaName, string schemaDefinition, CancellationToken ct = default
    ) {
        if (schemaName.IsUnset)
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDefinition);

        var request = new Contracts.RegisterSchemaVersionRequest {
            SchemaName       = schemaName,
            SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
        };

        try {
            var response = await ServiceClient
                .RegisterSchemaVersionAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            throw ex.ToException(
                // StatusCode fallbacks
                SchemaNotFoundException.Match(schemaName),
                SchemaDeletedException.Match(schemaName));
        }
    }

    #endregion
}
