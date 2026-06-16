using Kurrent.Client.Streams;

namespace Kurrent.Appender;

static class AppendRecordExtensions {
    const string OrderingKeyProperty = "$ordering-key";

    extension(AppendRecord record) {
        /// <summary>
        /// Gets the ordering key from the record's metadata, or null if not set.
        /// Records with the same ordering key are guaranteed to be appended in order.
        /// </summary>
        public string? GetOrderingKey() =>
            record.Metadata.TryGetValue<string>(OrderingKeyProperty, out var key) ? key : null;

        /// <summary>
        /// Sets the ordering key for this record and returns it. Records with the same ordering key
        /// are guaranteed to be appended in order by the <see cref="Appender"/>.
        /// The key is stored in the record's metadata as <c>$ordering-key</c>.
        /// Passing null is a no-op.
        /// </summary>
        public AppendRecord OrderingKey(string? key) {
            if (key is not null)
                record.Metadata.With(OrderingKeyProperty, key);

            return record;
        }
    }
}
