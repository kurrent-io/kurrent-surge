using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

public partial class RegistryClient {
    #region ->> CreateSchema <<-

    /// <summary>
    /// Creates a new schema in the registry with the provided details.
    /// </summary>
    /// <param name="schemaName">Unique name of the schema to create.</param>
    /// <param name="schemaDefinition">Schema definition in string format.</param>
    /// <param name="dataFormat">Format of the schema, such as Json, Protobuf, Avro, or Bytes.</param>
    /// <param name="compatibilityMode">Compatibility mode for the schema.</param>
    /// <param name="description">Human-readable description of the schema.</param>
    /// <param name="tags">Key-value pairs that provide additional metadata or categorization for the schema.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>A <see cref="SchemaVersionDescriptor"/> representing the newly created schema version.</returns>
    public async ValueTask<SchemaVersionDescriptor> CreateSchema(
        SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
        CompatibilityMode compatibilityMode, string description,
        IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
    ) {
        if (schemaName.IsUnset)
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDefinition);

        var details = new Contracts.SchemaDetails {
            DataFormat    = (Contracts.SchemaDataFormat)dataFormat,
            Compatibility = (Contracts.CompatibilityMode)compatibilityMode,
        };

        if (!string.IsNullOrEmpty(description))
            details.Description = description;

        foreach (var (key, value) in tags)
            details.Tags.Add(key, value);

        var request = new Contracts.CreateSchemaRequest {
            SchemaName       = schemaName,
            Details          = details,
            SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
        };

        try {
            var response = await ServiceClient
                .CreateSchemaAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            throw ex.ToException(
                SchemaAlreadyExistsException.From,

                // StatusCode fallbacks
                StatusCode.AlreadyExists.MapTo(new SchemaAlreadyExistsException(schemaName)));
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
