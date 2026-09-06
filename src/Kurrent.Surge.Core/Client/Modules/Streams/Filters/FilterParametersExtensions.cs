using Kurrent.Surge.RecordFilters;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Binds the SDK's own value types to a filter parameter. They live here rather than on
/// <see cref="FilterParameters"/> because the filter engine does not know about records.
/// </summary>
[PublicAPI]
public static class FilterParametersExtensions {
    extension(FilterParameters source) {
        /// <summary>
        /// Binds a record id as its 16 bytes, matching the <c>record_id</c> column. Bind
        /// <see cref="RecordId.ToGuid"/> instead when the expression casts the column to <c>UUID</c>.
        /// </summary>
        public FilterParameters Add(string name, RecordId value) =>
            source.Add(name, value.ToByteArray());

        public FilterParameters Add(string name, StreamName value) =>
            source.Add(name, value.Value);

        public FilterParameters Add(string name, LogPosition value) =>
            source.Add(name, value.Value);

        public FilterParameters Add(string name, Revision value) =>
            source.Add(name, value.Value);
    }
}
