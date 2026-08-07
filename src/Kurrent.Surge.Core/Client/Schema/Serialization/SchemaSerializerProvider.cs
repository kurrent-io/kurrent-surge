using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization;

/// <summary>
/// Defines functionality to provide schema serializers for specified data formats.
/// </summary>
public interface ISchemaSerializerProvider {
	/// <summary>
	/// Determines whether the specified schema data format is supported by the serializer provider.
	/// </summary>
	/// <param name="dataFormat">The schema data format to check for support.</param>
	/// <returns><c>true</c> if the specified schema data format is supported; otherwise, <c>false</c>.</returns>
	bool SupportsDataFormat(SchemaDataFormat dataFormat);

	/// <summary>
	/// Retrieves the serializer instance corresponding to the specified schema data format.
	/// </summary>
	/// <param name="dataFormat">The schema data format for which the serializer is required.</param>
	/// <returns>An instance of <see cref="ISchemaSerializer"/> corresponding to the specified schema data format.</returns>
	/// <exception cref="SchemaSerializerNotFoundException">
	/// Thrown when a serializer for the specified data format is not found.
	/// </exception>
	ISchemaSerializer GetSerializer(SchemaDataFormat dataFormat);
}

public sealed class SchemaSerializerProvider : ISchemaSerializerProvider {
	public SchemaSerializerProvider(IEnumerable<ISchemaSerializer> serializers) {
		Serializers = new Dictionary<SchemaDataFormat, ISchemaSerializer>();

		foreach (var serializer in serializers) {
			if (!Serializers.TryAdd(serializer.DataFormat, serializer))
				throw new DuplicateSchemaSerializerException(serializer.DataFormat, Serializers[serializer.DataFormat].GetType(), serializer.GetType());
		}
	}

	Dictionary<SchemaDataFormat, ISchemaSerializer> Serializers { get; }

	public bool SupportsDataFormat(SchemaDataFormat dataFormat) =>
		Serializers.ContainsKey(dataFormat);

	public ISchemaSerializer GetSerializer(SchemaDataFormat dataFormat) =>
		Serializers.TryGetValue(dataFormat, out var serializer)
			? serializer
			: throw new SchemaSerializerNotFoundException(dataFormat, Serializers.Keys.ToArray());
}

/// <summary>
/// Exception thrown when a schema serializer for a specified data format cannot be found.
/// </summary>
public class SchemaSerializerNotFoundException(SchemaDataFormat dataFormat, params SchemaDataFormat[] supportedSchemaDataFormats)
	: KurrentException(ErrorCode.FromType<SchemaSerializerNotFoundException>(), $"Unsupported schema data format. Expected one of {string.Join(", ", supportedSchemaDataFormats)} but got {dataFormat}.");

/// <summary>
/// Exception thrown when more than one serializer is registered for the same schema data format —
/// e.g. two opt-in packages that both implement <see cref="SchemaDataFormat.MessagePack"/>. Only one
/// serializer per data format is supported; pick one.
/// </summary>
public class DuplicateSchemaSerializerException(SchemaDataFormat dataFormat, Type existing, Type duplicate)
	: KurrentException(ErrorCode.FromType<DuplicateSchemaSerializerException>(), $"Multiple serializers registered for schema data format '{dataFormat}': '{existing.FullName}' and '{duplicate.FullName}'. Only one serializer per data format is supported.");
