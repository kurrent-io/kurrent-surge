using System.Runtime.CompilerServices;

namespace Kurrent.Client.Streams;

/// <summary>
/// Fluent builder for composing and executing global log read operations.
/// Created via <see cref="StreamsClient.Query()"/>.
/// </summary>
[PublicAPI]
public class LogQueryBuilder {
    readonly StreamsClient _client;
    ReadLogBuilder _inner = new();

    internal LogQueryBuilder(StreamsClient client) => _client = client;

    #region Configuration

    /// <summary>
    /// Sets the starting log position.
    /// </summary>
    public LogQueryBuilder From(LogPosition position) {
        _inner = _inner.From(position);
        return this;
    }

    /// <summary>
    /// Start reading from the earliest position in the log (forwards).
    /// </summary>
    public LogQueryBuilder FromStart() {
        _inner = _inner.From(LogPosition.Earliest).Forward();
        return this;
    }

    /// <summary>
    /// Start reading from the end of the log (backwards).
    /// </summary>
    public LogQueryBuilder FromEnd() {
        _inner = _inner.From(LogPosition.Latest).Backwards();
        return this;
    }

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public LogQueryBuilder Forward() {
        _inner = _inner.Forward();
        return this;
    }

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public LogQueryBuilder Backwards() {
        _inner = _inner.Backwards();
        return this;
    }

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public LogQueryBuilder WithMaxRecords(long maxRecords) {
        _inner = _inner.WithMaxRecords(maxRecords);
        return this;
    }

    /// <summary>
    /// Set the buffer capacity for streaming reads.
    /// </summary>
    public LogQueryBuilder WithBufferCapacity(int bufferCapacity) {
        _inner = _inner.WithBufferCapacity(bufferCapacity);
        return this;
    }

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public LogQueryBuilder WithFilter(IRecordFilter filter) {
        _inner = _inner.WithFilter(filter);
        return this;
    }

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public LogQueryBuilder WithFilterExpression(string expression) {
        _inner = _inner.WithFilterExpression(expression);
        return this;
    }

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public LogQueryBuilder SkipDecoding() {
        _inner = _inner.SkipDecoding();
        return this;
    }

    internal ReadOptions Build() => _inner.Build();

    #endregion

    #region Terminals

    /// <summary>
    /// Execute the query and return results as an async enumerable.
    /// </summary>
    public async IAsyncEnumerable<ReadRecordsResult> Read(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = _inner.WithCancellationToken(ct).Build();
        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            yield return result;
    }

    /// <summary>
    /// Execute the query and return the full <see cref="RecordStream"/> for backpressure monitoring.
    /// </summary>
    public ValueTask<RecordStream> ReadStream(CancellationToken ct = default) {
        var options = _inner.WithCancellationToken(ct).Build();
        return _client.ReadRecords(options);
    }

    /// <summary>
    /// Read the first record matching the query. Sets direction to forwards and maxRecords to 1.
    /// </summary>
    public async ValueTask<Record> ReadFirst(CancellationToken ct = default) {
        var options = _inner
            .Forward()
            .WithMaxRecords(1)
            .WithCancellationToken(ct)
            .Build();

        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            if (result.IsRecord)
                return result.AsRecord;

        return Record.None;
    }

    /// <summary>
    /// Read the last record matching the query. Sets direction to backwards and maxRecords to 1.
    /// </summary>
    public async ValueTask<Record> ReadLast(CancellationToken ct = default) {
        var options = _inner
            .Backwards()
            .WithMaxRecords(1)
            .WithCancellationToken(ct)
            .Build();

        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            if (result.IsRecord)
                return result.AsRecord;

        return Record.None;
    }

    #endregion
}

/// <summary>
/// Fluent builder for composing and executing stream-scoped read operations.
/// Created via <see cref="StreamsClient.Query(StreamName)"/>.
/// </summary>
[PublicAPI]
public class StreamQueryBuilder {
    readonly StreamsClient _client;
    ReadStreamBuilder _inner;

    internal StreamQueryBuilder(StreamsClient client, StreamName stream) {
        _client = client;
        _inner  = new ReadStreamBuilder().ForStream(stream);
    }

    #region Configuration

    /// <summary>
    /// Start reading from the beginning of the stream (forwards).
    /// </summary>
    public StreamQueryBuilder FromStart() {
        _inner = _inner.FromStart();
        return this;
    }

    /// <summary>
    /// Start reading from the end of the stream (backwards).
    /// </summary>
    public StreamQueryBuilder FromEnd() {
        _inner = _inner.FromEnd();
        return this;
    }

    /// <summary>
    /// Start reading from a specific stream revision.
    /// </summary>
    public StreamQueryBuilder FromRevision(Revision revision) {
        _inner = _inner.FromRevision(revision);
        return this;
    }

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public StreamQueryBuilder Forward() {
        _inner = _inner.Forward();
        return this;
    }

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public StreamQueryBuilder Backwards() {
        _inner = _inner.Backwards();
        return this;
    }

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public StreamQueryBuilder WithMaxRecords(long maxRecords) {
        _inner = _inner.WithMaxRecords(maxRecords);
        return this;
    }

    /// <summary>
    /// Set the buffer capacity for streaming reads.
    /// </summary>
    public StreamQueryBuilder WithBufferCapacity(int bufferCapacity) {
        _inner = _inner.WithBufferCapacity(bufferCapacity);
        return this;
    }

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public StreamQueryBuilder WithFilter(IRecordFilter filter) {
        _inner = _inner.WithFilter(filter);
        return this;
    }

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public StreamQueryBuilder WithFilterExpression(string expression) {
        _inner = _inner.WithFilterExpression(expression);
        return this;
    }

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public StreamQueryBuilder SkipDecoding() {
        _inner = _inner.SkipDecoding();
        return this;
    }

    internal ReadStreamOptions Build() => _inner.Build();

    #endregion

    #region Terminals

    /// <summary>
    /// Execute the query and return results as an async enumerable.
    /// </summary>
    public async IAsyncEnumerable<ReadRecordsResult> Read(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = _inner.WithCancellationToken(ct).Build();
        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            yield return result;
    }

    /// <summary>
    /// Execute the query and return the full <see cref="RecordStream"/> for backpressure monitoring.
    /// </summary>
    public ValueTask<RecordStream> ReadStream(CancellationToken ct = default) {
        var options = _inner.WithCancellationToken(ct).Build();
        return _client.ReadRecords(options);
    }

    /// <summary>
    /// Read the first record in the stream.
    /// </summary>
    public async ValueTask<Record> ReadFirst(CancellationToken ct = default) {
        var options = _inner
            .Forward()
            .WithMaxRecords(1)
            .WithCancellationToken(ct)
            .Build();

        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            if (result.IsRecord)
                return result.AsRecord;

        return Record.None;
    }

    /// <summary>
    /// Read the last record in the stream.
    /// </summary>
    public async ValueTask<Record> ReadLast(CancellationToken ct = default) {
        var options = _inner
            .Backwards()
            .WithMaxRecords(1)
            .WithCancellationToken(ct)
            .Build();

        await using var stream = await _client.ReadRecords(options);
        await foreach (var result in stream.WithCancellation(ct))
            if (result.IsRecord)
                return result.AsRecord;

        return Record.None;
    }

    #endregion
}
