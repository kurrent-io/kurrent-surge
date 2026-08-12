using System.Collections.Frozen;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization;

/// <summary>
/// Frozen format → codec registry.
/// </summary>
sealed class SchemaCodecProvider {
	readonly FrozenDictionary<SchemaDataFormat, ISchemaCodec> _codecs;

	public SchemaCodecProvider(IEnumerable<ISchemaCodec> codecs) {
		var registered = new Dictionary<SchemaDataFormat, ISchemaCodec>();

		foreach (var codec in codecs) {
			if (!registered.TryAdd(codec.DataFormat, codec))
				throw new DuplicateSchemaCodecException(codec.DataFormat, registered[codec.DataFormat].GetType(), codec.GetType());
		}

		_codecs = registered.ToFrozenDictionary();
	}

	public bool SupportsDataFormat(SchemaDataFormat dataFormat) =>
		_codecs.ContainsKey(dataFormat);

	public ISchemaCodec GetCodec(SchemaDataFormat dataFormat) =>
		_codecs.TryGetValue(dataFormat, out var codec)
			? codec
			: throw new SchemaCodecNotFoundException(dataFormat, _codecs.Keys.ToArray());
}

/// <summary>
/// Exception thrown when a codec for a specified data format is not registered.
/// </summary>
public class SchemaCodecNotFoundException(SchemaDataFormat dataFormat, params SchemaDataFormat[] supportedDataFormats)
	: KurrentException(ErrorCode.FromType<SchemaCodecNotFoundException>(), $"Unsupported schema data format. Expected one of {string.Join(", ", supportedDataFormats)} but got {dataFormat}.");

/// <summary>
/// Exception thrown when more than one codec is registered for the same schema data format —
/// e.g. two opt-in packages that both implement <see cref="SchemaDataFormat.MessagePack"/>. Only one
/// codec per data format is supported; pick one.
/// </summary>
public class DuplicateSchemaCodecException(SchemaDataFormat dataFormat, Type existing, Type duplicate)
	: KurrentException(ErrorCode.FromType<DuplicateSchemaCodecException>(), $"Multiple codecs registered for schema data format '{dataFormat}': '{existing.FullName}' and '{duplicate.FullName}'. Only one codec per data format is supported.");
