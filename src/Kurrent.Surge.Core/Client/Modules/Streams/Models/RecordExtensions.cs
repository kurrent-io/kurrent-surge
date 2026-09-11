namespace Kurrent.Surge.Client.Streams;

public static class RecordExtensions {
    extension(Record record) {
        /// <summary>
        /// The position to checkpoint, track, and resume from: the link's own position when a link
        /// delivered this record, otherwise <see cref="Record.Position"/>.
        /// </summary>
        /// <remarks>
        /// A resolved link's <see cref="Record.Position"/> is the target's, which sits earlier in the
        /// log than the link that delivered it. A sequence of delivered records is monotonic in this
        /// position and not in <see cref="Record.Position"/>.
        /// </remarks>
        public RecordPosition CheckpointPosition =>
            record.Link.IsUnset
                ? record.Position
                // The partition is assigned on Position by the data source after mapping; the link's
                // own position never carries one, so a tracker keyed by partition would reject it.
                : record.Link.Position with { Partition = record.Position.Partition };
    }
}
