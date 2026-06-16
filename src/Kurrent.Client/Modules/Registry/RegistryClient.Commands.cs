using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Schema;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

public partial class RegistryClient {
    #region ->> CreateSchema <<-

    /// <summary>
    /// Creates a new schema in the registry with the provided details.
    /// </summary>
    /// <param name="request">The schema details to register. Use <see cref="CreateSchemaRequest.For"/> to build one fluently.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionDescriptor"/> representing the newly created schema version.</returns>
    public async ValueTask<SchemaVersionDescriptor> CreateSchema(
        CreateSchemaRequest request, CancellationToken ct = default
    ) {
        request.EnsureValid();

        var details = new Contracts.SchemaDetails {
            DataFormat    = (Contracts.SchemaDataFormat)request.DataFormat,
            Compatibility = (Contracts.CompatibilityMode)request.CompatibilityMode,
        };

        if (!string.IsNullOrEmpty(request.Description))
            details.Description = request.Description;

        foreach (var (key, value) in request.Tags)
            details.Tags.Add(key, value);

        var rpc = new Contracts.CreateSchemaRequest {
            SchemaName       = request.SchemaName,
            Details          = details,
            SchemaDefinition = ByteString.CopyFromUtf8(request.SchemaDefinition)
        };

        try {
            var response = await ServiceClient
                .CreateSchemaAsync(rpc, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.AlreadyExists.MapTo(new SchemaAlreadyExistsException(request.SchemaName)));
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
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException(schemaName)));
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
                SchemaNotFoundException.From,
                SchemaDeletedException.From,
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.NotFound.MapTo(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition.MapTo(new SchemaDeletedException(schemaName)));
        }
    }

    #endregion
}
