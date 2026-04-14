// namespace Kurrent.Client.Streams;
//
// static partial class StreamsClientExtensions {
//     extension(StreamsClient client) {
//         #region . Query .
//
//         /// <summary>
//         /// Start composing a global log read query.
//         /// </summary>
//         internal LogQueryBuilder Query() => new(client);
//
//         /// <summary>
//         /// Start composing a stream-scoped read query.
//         /// </summary>
//         internal StreamQueryBuilder Query(StreamName stream) => new(client, stream);
//
//         #endregion
//     }
// }
//
// /// <summary>
// /// Fluent builder for composing and executing global log read operations.
// /// Created via <see cref="StreamsClient.Query()"/>.
// /// </summary>
// [PublicAPI]
// class LogQueryBuilder {
//     readonly StreamsClient _client;
//     ReadRecordsRequestBuilder _inner = new();
//
//     internal LogQueryBuilder(StreamsClient client) => _client = client;
//
//     #region Configuration
//
//     /// <summary>
//     /// Sets the starting log position.
//     /// </summary>
//     public LogQueryBuilder From(LogPosition position) {
//         _inner = _inner.FromPosition(position);
//         return this;
//     }
//
//     /// <summary>
//     /// Start reading from the earliest position in the log (forwards).
//     /// </summary>
//     public LogQueryBuilder FromStart() {
//         _inner = _inner.FromPosition(LogPosition.Earliest).Forward();
//         return this;
//     }
//
//     /// <summary>
//     /// Start reading from the end of the log (backwards).
//     /// </summary>
//     public LogQueryBuilder FromEnd() {
//         _inner = _inner.FromPosition(LogPosition.Latest).Backwards();
//         return this;
//     }
//
//     /// <summary>
//     /// Read forwards (toward newer records).
//     /// </summary>
//     public LogQueryBuilder Forward() {
//         _inner = _inner.Forward();
//         return this;
//     }
//
//     /// <summary>
//     /// Read backwards (toward older records).
//     /// </summary>
//     public LogQueryBuilder Backwards() {
//         _inner = _inner.Backwards();
//         return this;
//     }
//
//     /// <summary>
//     /// Limit the number of records returned.
//     /// </summary>
//     public LogQueryBuilder WithMaxRecords(long maxRecords) {
//         _inner = _inner.WithMaxRecords(maxRecords);
//         return this;
//     }
//
//     /// <summary>
//     /// Apply a record filter.
//     /// </summary>
//     public LogQueryBuilder WithFilter(IRecordFilter filter) {
//         _inner = _inner.WithFilter(filter);
//         return this;
//     }
//
//     /// <summary>
//     /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
//     /// </summary>
//     public LogQueryBuilder WithFilterExpression(string expression) {
//         _inner = _inner.WithFilterExpression(expression);
//         return this;
//     }
//
//     /// <summary>
//     /// Skip record data deserialization.
//     /// </summary>
//     public LogQueryBuilder SkipDecoding() {
//         _inner = _inner.SkipDecoding();
//         return this;
//     }
//
//     internal ReadRecordsRequest Build() => _inner.Build();
//
//     #endregion
//
//     #region Terminals
//
//     /// <summary>
//     /// Execute the query and return results as an async enumerable.
//     /// </summary>
//     public IAsyncEnumerable<ReadRecordsResult> Read(CancellationToken ct = default) {
//         var request = _inner.Build();
//         return _client.ReadRecords(request, ct);
//     }
//
//     /// <summary>
//     /// Read the first record matching the query. Sets direction to forwards and maxRecords to 1.
//     /// </summary>
//     public async ValueTask<Record> ReadFirst(CancellationToken ct = default) {
//         var request = _inner
//             .Forward()
//             .WithMaxRecords(1)
//             .Build();
//
//         await foreach (var result in _client.ReadRecords(request, ct).WithCancellation(ct))
//             if (result.IsRecord)
//                 return result.AsRecord;
//
//         return Record.None;
//     }
//
//     /// <summary>
//     /// Read the last record matching the query. Sets direction to backwards and maxRecords to 1.
//     /// </summary>
//     public async ValueTask<Record> ReadLast(CancellationToken ct = default) {
//         var request = _inner
//             .Backwards()
//             .WithMaxRecords(1)
//             .Build();
//
//         await foreach (var result in _client.ReadRecords(request, ct).WithCancellation(ct))
//             if (result.IsRecord)
//                 return result.AsRecord;
//
//         return Record.None;
//     }
//
//     #endregion
// }
//
// /// <summary>
// /// Fluent builder for composing and executing stream-scoped read operations.
// /// Created via <see cref="StreamsClient.Query(StreamName)"/>.
// /// </summary>
// [PublicAPI]
// class StreamQueryBuilder {
//     readonly StreamsClient _client;
//     ReadStreamRequestBuilder _inner;
//
//     internal StreamQueryBuilder(StreamsClient client, StreamName stream) {
//         _client = client;
//         _inner  = new ReadStreamRequestBuilder().ForStream(stream);
//     }
//
//     #region Configuration
//
//     /// <summary>
//     /// Start reading from the beginning of the stream (forwards).
//     /// </summary>
//     public StreamQueryBuilder FromStart() {
//         _inner = _inner.FromStart();
//         return this;
//     }
//
//     /// <summary>
//     /// Start reading from the end of the stream (backwards).
//     /// </summary>
//     public StreamQueryBuilder FromEnd() {
//         _inner = _inner.FromEnd();
//         return this;
//     }
//
//     /// <summary>
//     /// Start reading from a specific stream revision.
//     /// </summary>
//     public StreamQueryBuilder FromRevision(Revision revision) {
//         _inner = _inner.FromRevision(revision);
//         return this;
//     }
//
//     /// <summary>
//     /// Read forwards (toward newer records).
//     /// </summary>
//     public StreamQueryBuilder Forward() {
//         _inner = _inner.Forward();
//         return this;
//     }
//
//     /// <summary>
//     /// Read backwards (toward older records).
//     /// </summary>
//     public StreamQueryBuilder Backwards() {
//         _inner = _inner.Backwards();
//         return this;
//     }
//
//     /// <summary>
//     /// Limit the number of records returned.
//     /// </summary>
//     public StreamQueryBuilder WithMaxRecords(long maxRecords) {
//         _inner = _inner.WithMaxRecords(maxRecords);
//         return this;
//     }
//
//     /// <summary>
//     /// Apply a record filter.
//     /// </summary>
//     public StreamQueryBuilder WithFilter(IRecordFilter filter) {
//         _inner = _inner.WithFilter(filter);
//         return this;
//     }
//
//     /// <summary>
//     /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
//     /// </summary>
//     public StreamQueryBuilder WithFilterExpression(string expression) {
//         _inner = _inner.WithFilterExpression(expression);
//         return this;
//     }
//
//     /// <summary>
//     /// Skip record data deserialization.
//     /// </summary>
//     public StreamQueryBuilder SkipDecoding() {
//         _inner = _inner.SkipDecoding();
//         return this;
//     }
//
//     internal ReadStreamRequest Build() => _inner.Build();
//
//     #endregion
//
//     #region Terminals
//
//     /// <summary>
//     /// Execute the query and return results as an async enumerable.
//     /// </summary>
//     public IAsyncEnumerable<ReadRecordsResult> Read(CancellationToken ct = default) {
//         var request = _inner.Build();
//         return _client.ReadStream(request, ct);
//     }
//
//     /// <summary>
//     /// Read the first record in the stream.
//     /// </summary>
//     public async ValueTask<Record> ReadFirst(CancellationToken ct = default) {
//         var request = _inner
//             .Forward()
//             .WithMaxRecords(1)
//             .Build();
//
//         await foreach (var result in _client.ReadStream(request, ct).WithCancellation(ct))
//             if (result.IsRecord)
//                 return result.AsRecord;
//
//         return Record.None;
//     }
//
//     /// <summary>
//     /// Read the last record in the stream.
//     /// </summary>
//     public async ValueTask<Record> ReadLast(CancellationToken ct = default) {
//         var request = _inner
//             .Backwards()
//             .WithMaxRecords(1)
//             .Build();
//
//         await foreach (var result in _client.ReadStream(request, ct).WithCancellation(ct))
//             if (result.IsRecord)
//                 return result.AsRecord;
//
//         return Record.None;
//     }
//
//     #endregion
// }
//
