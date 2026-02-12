// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using static KurrentDB.Protocol.V2.Registry.CheckSchemaCompatibilityResponse;
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
    /// A <see cref="Result{SchemaVersionDescriptor, CreateSchemaError}"/> representing the outcome of the schema creation,
    /// which may contain the newly generated schema version or an error in case of failure.
    /// </returns>
    public async ValueTask<Result<SchemaVersionDescriptor, CreateSchemaError>> CreateSchema(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.AlreadyExists => CreateSchemaError.FromEx(new SchemaAlreadyExistsException(schemaName)),
                _                        => CreateSchemaError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return CreateSchemaError.FromEx(ex);
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
    /// A <see cref="Result{Schema, GetSchemaError}"/> that either contains the retrieved schema or an error indicating the issue encountered, such as the schema not being found.
    /// </returns>
    public async ValueTask<Result<Schema, GetSchemaError>> GetSchema(SchemaName schemaName, CancellationToken cancellationToken = default) {
        var request = new Contracts.GetSchemaRequest {
            SchemaName = schemaName
        };

        try {
            var response = await ServiceClient
                .GetSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Schema.ToSchema();
        }
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => GetSchemaError.FromEx(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition => GetSchemaError.FromEx(new SchemaDeletedException(schemaName)),
                _                             => GetSchemaError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return GetSchemaError.FromEx(ex);
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
    /// A <see cref="Result{SchemaVersion, GetSchemaVersionError}"/> containing either the schema version details or an error indicating that the schema could not be found.
    /// </returns>
    public async ValueTask<Result<SchemaVersion, GetSchemaVersionError>> GetSchemaVersion(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => GetSchemaVersionError.FromEx(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition => GetSchemaVersionError.FromEx(new SchemaDeletedException(schemaName)),
                _                             => GetSchemaVersionError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return GetSchemaVersionError.FromEx(ex);
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
    /// A <see cref="Result{SchemaVersion, GetSchemaVersionError}"/> containing the requested schema version details or an error
    /// if the schema version is not found.
    /// </returns>
    public async ValueTask<Result<SchemaVersion, GetSchemaVersionError>> GetSchemaVersionById(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => GetSchemaVersionError.FromEx(new SchemaNotFoundException(rex.Message)),
                StatusCode.FailedPrecondition => GetSchemaVersionError.FromEx(new SchemaDeletedException(rex.Message)),
                _                             => GetSchemaVersionError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return GetSchemaVersionError.FromEx(ex);
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
    /// A <see cref="Result{Success, DeleteSchemaError}"/> indicating the result of the schema deletion operation.
    /// The result can either indicate success or specify that the schema was not found.
    /// </returns>
    public async ValueTask<Result<Success, DeleteSchemaError>> DeleteSchema(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => DeleteSchemaError.FromEx(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition => DeleteSchemaError.FromEx(new SchemaDeletedException(schemaName)),
                _                             => DeleteSchemaError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return DeleteSchemaError.FromEx(ex);
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
    /// A <see cref="Result{TSuccess, TError}"/> of type <see cref="SchemaVersionId"/> on success, or
    /// <see cref="CheckSchemaCompatibilityError"/> on failure indicating details of the compatibility issues.
    /// </returns>
    public async ValueTask<Result<SchemaVersionId, CheckSchemaCompatibilityError>> CheckSchemaCompatibility(
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

            if (response.ResultCase is ResultOneofCase.Success)
                return SchemaVersionId.From(response.Success.SchemaVersionId);

            CheckSchemaCompatibilityError error = new SchemaIncompatibleException(SchemaCompatibilityErrors.FromProto(response.Failure.Errors));
            return error;
        }
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => CheckSchemaCompatibilityError.FromEx(new SchemaNotFoundException(rex.Message)),
                StatusCode.FailedPrecondition => CheckSchemaCompatibilityError.FromEx(new SchemaDeletedException(rex.Message)),
                _                             => CheckSchemaCompatibilityError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return CheckSchemaCompatibilityError.FromEx(ex);
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
    /// A Result containing either the list of matching schemas or an error indicating the issue encountered.
    /// </returns>
    public async ValueTask<Result<IReadOnlyList<Schema>, ListSchemasError>> ListSchemas(
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

            return Result<IReadOnlyList<Schema>, ListSchemasError>.Success(response.ToSchemas());
        }
        catch (RpcException rex) {
            return ListSchemasError.FromEx(rex);
        }
        catch (Exception ex) {
            return ListSchemasError.FromEx(ex);
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
    /// A <see cref="Result{SchemaVersionDescriptor, RegisterSchemaVersionError}"/> representing the outcome,
    /// which may contain the newly registered schema version descriptor or an error in case of failure.
    /// </returns>
    public async ValueTask<Result<SchemaVersionDescriptor, RegisterSchemaVersionError>> RegisterSchemaVersion(
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
        catch (RpcException rex) {
            return rex.StatusCode switch {
                StatusCode.NotFound           => RegisterSchemaVersionError.FromEx(new SchemaNotFoundException(schemaName)),
                StatusCode.FailedPrecondition => RegisterSchemaVersionError.FromEx(new SchemaDeletedException(schemaName)),
                _                             => RegisterSchemaVersionError.FromEx(rex)
            };
        }
        catch (Exception ex) {
            return RegisterSchemaVersionError.FromEx(ex);
        }
    }

}
