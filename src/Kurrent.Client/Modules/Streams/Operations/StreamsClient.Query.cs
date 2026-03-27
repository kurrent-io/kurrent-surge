namespace Kurrent.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        #region . Query .

        /// <summary>
        /// Start composing a global log read query.
        /// </summary>
        public LogQueryBuilder Query() => new(client);

        /// <summary>
        /// Start composing a stream-scoped read query.
        /// </summary>
        public StreamQueryBuilder Query(StreamName stream) => new(client, stream);

        #endregion
    }
}
