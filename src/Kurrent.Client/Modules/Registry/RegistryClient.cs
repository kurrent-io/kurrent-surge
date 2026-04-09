// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

using static KurrentDB.Protocol.V2.Registry.SchemaRegistryService;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

public class RegistryClient : ClientModuleBase {
    internal RegistryClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.LegacyCallInvoker);

    internal SchemaRegistryServiceClient ServiceClient { get; }

    /// <summary>
    /// Creates a new schema in the registry with the provided details.
    /// </summary>
    /// <param name="schemaName">
    /// The unique name of the schema to create.
    /// </param>
    /// <param name="schemaDefinition">
    /// The schema definition in string format, representing the structure of the schema.
    /// </param>
    /// <param name="dataFormat">
    /// The format of the schema, such as Json, Protobuf, Avro, or Bytes.
    /// </param>
    /// <param name="compatibilityMode">
    /// Specifies the compatibility mode for the schema. Defaults to <see cref="Registry.CompatibilityMode.None"/>.
    /// </param>
    /// <param name="description">
    /// An optional human-readable description of the schema being created.
    /// </param>
    /// <param name="tags">
    /// An optional dictionary of key-value pairs that provide additional metadata or categorization for the schema.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="SchemaVersionDescriptor"/> representing the newly generated schema version.
    /// </returns>
    public async ValueTask<SchemaVersionDescriptor> CreateSchema(
        SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
        CompatibilityMode compatibilityMode = CompatibilityMode.None,
        string description = "",
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default
    ) {
        var details = new Contracts.SchemaDetails {
            DataFormat    = (Contracts.SchemaDataFormat)dataFormat,
            Compatibility = (Contracts.CompatibilityMode)compatibilityMode,
        };

        if (!string.IsNullOrEmpty(description))
            details.Description = description;

        if (tags is not null)
            foreach (var tag in tags)
                details.Tags.Add(tag.Key, tag.Value);

        var request = new Contracts.CreateSchemaRequest {
            SchemaName       = schemaName,
            Details          = details,
            SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
        };

        try {
            var response = await ServiceClient
                .CreateSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            var mex = ex.AsWellKnownException(SchemaAlreadyExistsException.From);

            if (mex is not SchemaAlreadyExistsException && ex is RpcException { StatusCode: StatusCode.AlreadyExists })
                throw new SchemaAlreadyExistsException(schemaName);

            throw mex;
        }
    }

    /// <summary>
    /// Retrieves the schema details for the specified schema name from the registry.
    /// </summary>
    /// <param name="schemaName">
    /// The unique name of the schema to retrieve.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Schema"/> containing the retrieved schema details.
    /// </returns>
    public async ValueTask<Schema> GetSchema(SchemaName schemaName, CancellationToken cancellationToken = default) {
        var request = new Contracts.GetSchemaRequest {
            SchemaName = schemaName
        };

        try {
            var response = await ServiceClient
                .GetSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Schema.ToSchema();
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException(schemaName),
                new SchemaDeletedException(schemaName));
        }
    }

    /// <summary>
    /// Retrieves the specific version of a schema from the registry based on the provided details.
    /// </summary>
    /// <param name="schemaName">
    /// The unique identifier of the schema whose version is being retrieved.
    /// </param>
    /// <param name="versionNumber">
    /// The version number of the schema to retrieve. If not provided, the latest version will be fetched.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="SchemaVersion"/> containing the schema version details.
    /// </returns>
    public async ValueTask<SchemaVersion> GetSchemaVersion(
        SchemaName schemaName,
        int? versionNumber = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.GetSchemaVersionRequest { SchemaName = schemaName };

        if (versionNumber.HasValue)
            request.VersionNumber = versionNumber.Value;

        try {
            var response = await ServiceClient
                .GetSchemaVersionAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Version.ToSchemaVersion();
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException(schemaName),
                new SchemaDeletedException(schemaName));
        }
    }

    /// <summary>
    /// Retrieves the schema version details for the specified schema version ID from the registry.
    /// </summary>
    /// <param name="schemaVersionId">
    /// A <see cref="SchemaVersionId"/> that represents the unique identifier of the schema version to retrieve.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="SchemaVersion"/> containing the requested schema version details.
    /// </returns>
    public async ValueTask<SchemaVersion> GetSchemaVersionById(
        SchemaVersionId schemaVersionId,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.GetSchemaVersionByIdRequest { SchemaVersionId = schemaVersionId };

        try {
            var response = await ServiceClient
                .GetSchemaVersionByIdAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Version.ToSchemaVersion();
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException($"Schema version '{schemaVersionId}' was not found."),
                new SchemaDeletedException($"Schema for version '{schemaVersionId}' has been deleted."));
        }
    }

    /// <summary>
    /// Deletes an existing schema in the registry identified by the specified name, and all its versions.
    /// </summary>
    /// <param name="schemaName">
    /// The unique name of the schema to delete.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="Success"/> indicating the schema was deleted successfully.
    /// </returns>
    public async ValueTask<Success> DeleteSchema(
        SchemaName schemaName,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.DeleteSchemaRequest { SchemaName = schemaName };

        try {
            _ = await ServiceClient
                .DeleteSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException(schemaName),
                new SchemaDeletedException(schemaName));
        }
    }

    /// <summary>
    /// Checks the compatibility of a schema against a given schema identifier and data format.
    /// </summary>
    /// <param name="identifier">
    /// The schema identifier, which can either be a schema name or a schema version ID.
    /// </param>
    /// <param name="schemaDefinition">
    /// The schema definition to check for compatibility in string format.
    /// </param>
    /// <param name="dataFormat">
    /// The format of the schema, such as Json, Protobuf, Avro, or Bytes.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="SchemaVersionId"/> on success, indicating the compatible schema version.
    /// </returns>
    public async ValueTask<SchemaVersionId> CheckSchemaCompatibility(
        SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat,
        CancellationToken cancellationToken = default
    ) {
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
                .CheckSchemaCompatibilityAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (response.ResultCase is Contracts.CheckSchemaCompatibilityResponse.ResultOneofCase.Success)
                return SchemaVersionId.From(response.Success.SchemaVersionId);

            throw new SchemaIncompatibleException(SchemaCompatibilityErrors.FromProto(response.Failure.Errors));
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException($"Schema '{identifier}' was not found."),
                new SchemaDeletedException($"Schema '{identifier}' has been deleted."));
        }
    }

    /// <summary>
    /// Lists schemas in the registry, optionally filtered by name prefix and tags.
    /// </summary>
    /// <param name="schemaNamePrefix">
    /// An optional prefix to filter schemas by name. Only schemas whose names start with this prefix are returned.
    /// </param>
    /// <param name="tags">
    /// An optional dictionary of key-value pairs to filter schemas by tags.
    /// Only schemas matching all specified tags are returned.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// An <see cref="IReadOnlyList{Schema}"/> containing the matching schemas.
    /// </returns>
    public async ValueTask<IReadOnlyList<Schema>> ListSchemas(
        string? schemaNamePrefix = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.ListSchemasRequest();

        if (schemaNamePrefix is not null)
            request.SchemaNamePrefix = schemaNamePrefix;

        if (tags is not null)
            foreach (var tag in tags)
                request.SchemaTags.Add(tag.Key, tag.Value);

        try {
            var response = await ServiceClient
                .ListSchemasAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.ToSchemas();
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    /// <summary>
    /// Registers a new version of an existing schema in the registry.
    /// The server validates the schema definition against the schema's configured compatibility mode.
    /// </summary>
    /// <param name="schemaName">
    /// The unique name of the schema to register a new version for.
    /// </param>
    /// <param name="schemaDefinition">
    /// The schema definition in string format for the new version.
    /// </param>
    /// <param name="cancellationToken">
    /// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
    /// </param>
    /// <returns>
    /// A <see cref="SchemaVersionDescriptor"/> representing the newly registered schema version.
    /// </returns>
    public async ValueTask<SchemaVersionDescriptor> RegisterSchemaVersion(
        SchemaName schemaName, string schemaDefinition,
        CancellationToken cancellationToken = default
    ) {
        var request = new Contracts.RegisterSchemaVersionRequest {
            SchemaName       = schemaName,
            SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
        };

        try {
            var response = await ServiceClient
                .RegisterSchemaVersionAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.ToSchemaVersionDescriptor();
        }
        catch (Exception ex) {
            throw ToSchemaException(ex,
                new SchemaNotFoundException(schemaName),
                new SchemaDeletedException(schemaName));
        }
    }

    // The server does not yet send ErrorInfo. Remove when the server sends structured error details
    static Exception ToSchemaException(
        Exception ex,
        SchemaNotFoundException notFound,
        SchemaDeletedException deleted
    ) {
        var mex = ex.AsWellKnownException(
            SchemaNotFoundException.From,
            SchemaDeletedException.From);

        if (mex is SchemaNotFoundException or SchemaDeletedException)
            return mex;

        return ex switch {
            RpcException { StatusCode: StatusCode.NotFound }           => notFound,
            RpcException { StatusCode: StatusCode.FailedPrecondition } => deleted,
            _                                                          => mex
        };
    }
}
