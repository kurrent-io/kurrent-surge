// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using Google.Protobuf;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using static KurrentDB.Protocol.V2.Registry.SchemaRegistryService;
using Contracts = KurrentDB.Protocol.V2.Registry;

namespace Kurrent.Client.Registry;

public class RegistryClient : ClientModuleBase {
    internal RegistryClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.LegacyCallInvoker);

    SchemaRegistryServiceClient ServiceClient { get; }

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
	/// Specifies the compatibility mode for the schema, which defines whether and how new versions
	/// of the schema need to maintain compatibility with previous versions.
	/// </param>
	/// <param name="description">
	/// A human-readable description of the schema being created.
	/// </param>
	/// <param name="tags">
	/// A dictionary of key-value pairs that provide additional metadata or categorization for the schema.
	/// </param>
	/// <param name="cancellationToken">
	/// An optional <see cref="CancellationToken"/> to observe while waiting for the operation to complete.
	/// </param>
	/// <returns>
	/// A <see cref="SchemaVersionDescriptor"/> representing the newly generated schema version.
	/// </returns>
	/// <exception cref="Exception">
	/// Throws an exception if the operation encounters an error, such as a network issue or invalid input.
	/// </exception>
	public async ValueTask<SchemaVersionDescriptor> CreateSchema(
		SchemaName schemaName, string schemaDefinition,
		SchemaDataFormat dataFormat, CompatibilityMode compatibilityMode,
		string description, Dictionary<string, string> tags,
		CancellationToken cancellationToken = default
	) {
		var request = new Contracts.CreateSchemaRequest {
			SchemaName = schemaName,
			Details    = new() {
				DataFormat    = (Contracts.SchemaDataFormat)dataFormat,
				Compatibility = (Contracts.CompatibilityMode)compatibilityMode,
				Description   = description,
				Tags          = { tags }
			},
			SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition)
		};

        try {
            var response = await ServiceClient
                .CreateSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new SchemaVersionDescriptor(Guid.Parse(response.SchemaVersionId), response.VersionNumber);
        }
        catch (Exception ex) {
            throw ex.ToException();
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
	/// <exception cref="Exception">
	/// Throws an exception if an error occurs while trying to retrieve the schema, including issues with network connectivity or invalid input.
	/// </exception>
	public async ValueTask<Schema> GetSchema(SchemaName schemaName, CancellationToken cancellationToken = default) {
		var request = new Contracts.GetSchemaRequest {
			SchemaName = schemaName
		};

        try {
            var response = await ServiceClient
                .GetSchemaAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Schema.FromProto(response.Schema);
        }
        catch (Exception ex) {
            throw ex.ToException();
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
	/// <exception cref="Exception">
	/// Throws an exception if an error occurs during the operation, such as network issues or invalid input.
	/// </exception>
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

            return SchemaVersion.FromProto(response.Version);
        }
        catch (Exception ex) {
            throw ex.ToException();
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
	/// <exception cref="RpcException">
	/// Throws an exception in case of communication errors with the schema registry service.
	/// </exception>
	public async ValueTask<SchemaVersion> GetSchemaVersionById(
		SchemaVersionId schemaVersionId,
        CancellationToken cancellationToken = default
	) {
		var request = new Contracts.GetSchemaVersionByIdRequest { SchemaVersionId = schemaVersionId };

        try {
            var response = await ServiceClient
                .GetSchemaVersionByIdAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return SchemaVersion.FromProto(response.Version);
        }
        catch (Exception ex) {
            throw ex.ToException();
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
	/// A <see cref="Success"/> indicating the schema was successfully deleted.
	/// </returns>
	/// <exception cref="Exception">
	/// Throws an exception if the operation encounters an error, such as a network issue or invalid input.
	/// </exception>
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
            throw ex.ToException();
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
	/// A <see cref="SchemaVersionId"/> on success indicating the compatible schema version.
	/// </returns>
	/// <exception cref="Exception">
	/// Throws an exception if there is an error during the compatibility check process.
	/// </exception>
	public async ValueTask<SchemaVersionId> CheckSchemaCompatibility(
		SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat, CancellationToken cancellationToken = default
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

            // how do we model the SchemaCompatibilityErrors? is it a part of the success value? or do we model it as a failure?
            // return response.Success is not null
            //     ? Result<SchemaVersionId, CheckSchemaCompatibilityError>.Success(SchemaVersionId.From(response.Success.SchemaVersionId))
            //     : Result<SchemaVersionId, CheckSchemaCompatibilityError>.Failure(SchemaCompatibilityErrors.FromProto(response.Failure.Errors));

            throw new NotImplementedException();
        }

        catch (Exception ex) {
            throw ex.ToException();
        }
        // catch (RpcException rex) {
        //     return Result.Failure<SchemaVersionId, CheckSchemaCompatibilityError>(rex.StatusCode switch {
        //         StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
        //         StatusCode.NotFound         => new ErrorDetails.NotFound(m => m.With(identifier.IsSchemaName ? "SchemaName" : "SchemaVersionId", identifier)),
        //         _                           => throw rex.WithOriginalCallStack()
        //     });
        // }
	}
}
