using System.Collections.Frozen;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Schema.Governance;

/// <summary>
/// The result of catalog registration: the wire schema info for every registered or
/// verified catalog entry, keyed by message type.
/// </summary>
/// <remarks>
/// Not keyed by data format. A schema name carries one version lineage whatever the payload
/// encoding, which is what lets a producer change format without changing the contract a
/// consumer validates against.
/// </remarks>
sealed class CatalogSchemaMap {
	public static readonly CatalogSchemaMap Empty = new(FrozenDictionary<Type, RecordSchemaInfo>.Empty);

	CatalogSchemaMap(FrozenDictionary<Type, RecordSchemaInfo> entries) => _entries = entries;

	readonly FrozenDictionary<Type, RecordSchemaInfo> _entries;

	public int Count => _entries.Count;

	public bool TryGet(Type messageType, out RecordSchemaInfo info) =>
		_entries.TryGetValue(messageType, out info!);

	public sealed class Builder {
		readonly Dictionary<Type, RecordSchemaInfo> _entries = [];

		public void Add(Type messageType, RecordSchemaInfo info) =>
			_entries[messageType] = info;

		public CatalogSchemaMap Build() => new(_entries.ToFrozenDictionary());
	}
}
