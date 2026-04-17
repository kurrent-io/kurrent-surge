using Kurrent.Client.Streams;

namespace Kurrent.Processors.Testing;

/// <summary>
/// Test helpers for creating decoded records. Visible to test projects via InternalsVisibleTo.
/// </summary>
static class RecordTestExtensions {
	/// <summary>
	/// Returns a copy of the record with Value and ValueType set as if it had been decoded.
	/// </summary>
	public static Record WithDecodedValue<T>(this Record record, T value) where T : notnull {
		record.Value     = value;
		record.ValueType = typeof(T);
		return record;
	}
}
