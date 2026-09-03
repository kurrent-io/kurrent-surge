using System.Collections.Frozen;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Schema.Governance;

/// <summary>
/// The result of catalog registration: the wire schema info for every registered or
/// verified catalog entry, keyed by message type and data format.
/// </summary>
sealed class CatalogSchemaMap {
	public static readonly CatalogSchemaMap Empty = new(FrozenDictionary<(Type, SchemaDataFormat), RecordSchemaInfo>.Empty);

	CatalogSchemaMap(FrozenDictionary<(Type, SchemaDataFormat), RecordSchemaInfo> entries) => _entries = entries;

	readonly FrozenDictionary<(Type, SchemaDataFormat), RecordSchemaInfo> _entries;

	public int Count => _entries.Count;

	public bool TryGet(Type messageType, SchemaDataFormat format, out RecordSchemaInfo info) =>
		_entries.TryGetValue((messageType, format), out info!);

	public sealed class Builder {
		readonly Dictionary<(Type, SchemaDataFormat), RecordSchemaInfo> _entries = [];

		public void Add(Type messageType, RecordSchemaInfo info) =>
			_entries[(messageType, info.DataFormat)] = info;

		public CatalogSchemaMap Build() => new(_entries.ToFrozenDictionary());
	}
}
