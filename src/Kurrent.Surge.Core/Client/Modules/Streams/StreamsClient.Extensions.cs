namespace Kurrent.Surge.Client.Streams;

public static partial class StreamsClientExtensions {
    extension(IStreamAppender client) {
        #region ->> AppendRecords <<-

        /// <inheritdoc cref="IStreamAppender.AppendRecords(IAsyncEnumerable{AppendRecord}, AppendRecordsOptions?, CancellationToken)"/>
        public ValueTask<AppendRecordsResponse> AppendRecords(
            IAsyncEnumerable<AppendRecord> records,
            CancellationToken cancellationToken
        ) => client.AppendRecords(records, (AppendRecordsOptions?)null, cancellationToken);

        /// <inheritdoc cref="IStreamAppender.AppendRecords(IAsyncEnumerable{AppendRecord}, AppendRecordsOptions?, CancellationToken)"/>
        public ValueTask<AppendRecordsResponse> AppendRecords(
            IEnumerable<AppendRecord> records,
            AppendRecordsOptions? options,
            CancellationToken cancellationToken
        ) => client.AppendRecords(records.ToAsyncEnumerable(), options, cancellationToken);

        /// <inheritdoc cref="IStreamAppender.AppendRecords(IAsyncEnumerable{AppendRecord}, AppendRecordsOptions?, CancellationToken)"/>
        public ValueTask<AppendRecordsResponse> AppendRecords(
            IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken
        ) => client.AppendRecords(records.ToAsyncEnumerable(), (AppendRecordsOptions?)null, cancellationToken);

        /// <inheritdoc cref="IStreamAppender.AppendRecords(IAsyncEnumerable{AppendRecord}, Func{AppendRecordsOptions, AppendRecordsOptions}, CancellationToken)"/>
        public ValueTask<AppendRecordsResponse> AppendRecords(
            IEnumerable<AppendRecord> records,
            Func<AppendRecordsOptions, AppendRecordsOptions> configure,
            CancellationToken cancellationToken
        ) => client.AppendRecords(records.ToAsyncEnumerable(), configure, cancellationToken);

        #endregion

        #region ->> AppendStream <<-

        /// <inheritdoc cref="IStreamAppender.AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
        public ValueTask<AppendStreamResponse> AppendStream(
            StreamName stream,
            IAsyncEnumerable<AppendRecord> records,
            CancellationToken cancellationToken
        ) => client.AppendStream(stream, records, ExpectedStreamState.Any, cancellationToken);

        /// <inheritdoc cref="IStreamAppender.AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
        public ValueTask<AppendStreamResponse> AppendStream(
            StreamName stream,
            IEnumerable<AppendRecord> records,
            ExpectedStreamState expectedState,
            CancellationToken cancellationToken = default
        ) => client.AppendStream(stream, records.ToAsyncEnumerable(), expectedState, cancellationToken);

        /// <inheritdoc cref="IStreamAppender.AppendStream(StreamName, IAsyncEnumerable{AppendRecord}, ExpectedStreamState, CancellationToken)"/>
        public ValueTask<AppendStreamResponse> AppendStream(
            StreamName stream,
            IEnumerable<AppendRecord> records,
            CancellationToken cancellationToken = default
        ) => client.AppendStream(stream, records.ToAsyncEnumerable(), ExpectedStreamState.Any, cancellationToken);

        /// <summary>
        /// Appends a single record to its own <see cref="AppendRecord.Stream"/>, guarding on
        /// <paramref name="expectedState"/>.
        /// </summary>
        public ValueTask<AppendStreamResponse> AppendStream(
            AppendRecord record,
            ExpectedStreamState expectedState,
            CancellationToken cancellationToken = default
        ) => client.AppendStream(record.Stream, new[] { record }.ToAsyncEnumerable(), expectedState, cancellationToken);

        /// <inheritdoc cref="AppendStream(AppendRecord, ExpectedStreamState, CancellationToken)"/>
        public ValueTask<AppendStreamResponse> AppendStream(
            AppendRecord record,
            CancellationToken cancellationToken = default
        ) => client.AppendStream(record.Stream, new[] { record }.ToAsyncEnumerable(), ExpectedStreamState.Any, cancellationToken);

        #endregion
    }

    extension(IStreamManager client) {
        #region ->> Delete / Tombstone <<-

        /// <inheritdoc cref="IStreamManager.Delete(StreamName, Revision, CancellationToken)"/>
        public ValueTask<LogPosition> Delete(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Delete(stream, Revision.Unset, cancellationToken);

        /// <inheritdoc cref="IStreamManager.Tombstone(StreamName, Revision, CancellationToken)"/>
        public ValueTask<LogPosition> Tombstone(StreamName stream, CancellationToken cancellationToken = default) =>
            client.Tombstone(stream, Revision.Unset, cancellationToken);

        #endregion

        #region ->> SetCustomMetadata <<-

        /// <inheritdoc cref="IStreamManager.SetCustomMetadata(StreamName, Action{Metadata}, CancellationToken)"/>
        public ValueTask<Revision> SetCustomMetadata(StreamName stream, Metadata metadata, CancellationToken cancellationToken = default) =>
            client.SetCustomMetadata(stream, x => x.Reset(metadata), cancellationToken);

        #endregion

        #region ->> ConfigureDataRetention <<-

        /// <inheritdoc cref="IStreamManager.ConfigureDataRetention(StreamName, Action{StreamRetentionOptions}, CancellationToken)"/>
        public ValueTask<Revision> ConfigureDataRetention(StreamName stream, StreamRetention options, CancellationToken cancellationToken = default) =>
            client.ConfigureDataRetention(stream, x => { x.MaxCount = options.MaxCount; x.MaxAge = options.MaxAge; }, cancellationToken);

        /// <inheritdoc cref="IStreamManager.ConfigureDataRetention(StreamName, Action{StreamRetentionOptions}, CancellationToken)"/>
        public ValueTask<Revision> ConfigureDataRetention(StreamName stream, int maxCount, CancellationToken cancellationToken = default) =>
            client.ConfigureDataRetention(stream, x => x.MaxCount = maxCount, cancellationToken);

        /// <inheritdoc cref="IStreamManager.ConfigureDataRetention(StreamName, Action{StreamRetentionOptions}, CancellationToken)"/>
        public ValueTask<Revision> ConfigureDataRetention(StreamName stream, TimeSpan maxAge, CancellationToken cancellationToken = default) =>
            client.ConfigureDataRetention(stream, x => x.MaxAge = maxAge, cancellationToken);

        #endregion
    }
}
