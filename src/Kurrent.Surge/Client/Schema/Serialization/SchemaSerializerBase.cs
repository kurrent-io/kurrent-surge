#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

using System.Diagnostics;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Client.Schema.Serialization;

public abstract class SchemaSerializerBase(SchemaSerializerOptions options, ISchemaManager schemaManager) : ISchemaSerializer {
	SchemaSerializerOptions Options       { get; } = options;
	ISchemaManager          SchemaManager { get; } = schemaManager;

	public abstract SchemaDataFormat DataFormat { get; }

	async ValueTask<ReadOnlyMemory<byte>> ISchemaSerializer.Serialize(object? value, SchemaSerializationContext context) {
		// -------------------------------------------------------------------------------------------------
		// these debug asserts ensure the required info is set during the development process
		// -------------------------------------------------------------------------------------------------
		Debug.Assert(!string.IsNullOrWhiteSpace(context.Stream) || context.RecordSchema.HasSchema, "Stream name is missing in the context");
		Debug.Assert(context.RecordSchema.DataFormat == DataFormat, "Schema format does not match the serializer data format");
		// -------------------------------------------------------------------------------------------------
       
		if (value is null)
			return ReadOnlyMemory<byte>.Empty;

		var policy = context.SchemaRegistryPolicy
			.Resolve(Options.SchemaRegistration, context.Stream, DataFormat)
			.EnsureDataFormatCompliance(DataFormat);
        
		var messageType = value.GetType();

		try {
			var result = await SchemaManager
				.RegisterOrValidateSchema(messageType, policy, context.CancellationToken)
				.ConfigureAwait(false);

            context.RecordSchema = result.SchemaVersionId == SchemaVersionId.Unset
                ? context.RecordSchema with {
                    Schema     = result.SchemaName,
                    DataFormat = DataFormat
                }
                : context.RecordSchema with {
                    Schema     = result.SchemaName,
                    DataFormat = DataFormat,
                    VersionId  = result.SchemaVersionId
                };

            return Serialize(value);
		}
		// SchemaNameNotMappedException passes through unwrapped: an unmapped type is a
		// configuration error with its own remediation message, not a serialization failure.
		catch (Exception ex) when (ex is not SchemaSerializationException and not SchemaNameNotMappedException) {
			throw new SchemaSerializationFailedException(DataFormat, messageType,  ex);
		}
	}

	async ValueTask<DeserializationResult> ISchemaSerializer.Deserialize(ReadOnlyMemory<byte> data, SchemaSerializationContext context) {
        var schemaInfo = context.RecordSchema;

		// -------------------------------------------------------------------------------------------------
		// these debug asserts ensure the required info is set during the development process
		// -------------------------------------------------------------------------------------------------
		Debug.Assert(schemaInfo.HasSchema, "Schema name is missing in the context");
		Debug.Assert(schemaInfo.HasDataFormat, "Schema format is missing in the context");
		Debug.Assert(schemaInfo.DataFormat == DataFormat, "Schema format does not match the serializer data format");
		// -------------------------------------------------------------------------------------------------

		if (data.IsEmpty)
			return DeserializationResult.NoData.Instance;

		try {
			var policy = context.SchemaRegistryPolicy
				.Resolve(Options.SchemaRegistration, context.Stream, DataFormat)
				.EnsureDataFormatCompliance(DataFormat);

			var result = await SchemaManager
				.ValidateAndEnsureSchemaCompatibility(schemaInfo, policy, context.CancellationToken)
				.ConfigureAwait(false);

            // Unresolved schema name: the record passes through undeserialized — a
            // forward-unknown type is data for fallback handlers, not an error. The caller
            // (read/subscribe options) decides whether unmapped is worth throwing for.
            if (result.MessageType is null)
                return DeserializationResult.UnknownType.Instance;

            // set the schema version id if missing
            if(result.SchemaVersionId != SchemaVersionId.Unset)
                context.RecordSchema = context.RecordSchema with {
                    VersionId = result.SchemaVersionId
                };

			return new DeserializationResult.Deserialized(Deserialize(data, result.MessageType));
		}
		catch (Exception ex) when (ex is not SchemaSerializationException) {
            // var wtfpolicy = context.SchemaRegistryPolicy
            //     .Resolve(Options.SchemaRegistration, context.Stream, DataFormat)
            //     .EnsureDataFormatCompliance(DataFormat);
            //
            // var wtfvalidateresult = await SchemaManager
            //     .ValidateAndEnsureSchemaCompatibility(schemaInfo, wtfpolicy, context.CancellationToken)
            //     .ConfigureAwait(false);
            
			throw new SchemaDeserializationFailedException(DataFormat, schemaInfo.Schema,  ex);
		}
	}

	/// <summary>
	/// Serializes the given object to a byte array representation.
	/// </summary>
	/// <param name="value">The object to be serialized. Can be null.</param>
	/// <returns>A read-only memory containing the serialized byte array of the object.</returns>
	protected abstract ReadOnlyMemory<byte> Serialize(object? value);

	/// <summary>
	/// Deserializes the given byte array representation to an object of the specified type.
	/// </summary>
	/// <param name="data">The memory segment containing the serialized byte array of the object.</param>
	/// <param name="resolvedType">The target type to which the byte array should be deserialized.</param>
	/// <returns>The deserialized object of the specified type.</returns>
	protected abstract object Deserialize(ReadOnlyMemory<byte> data, Type resolvedType);
}
