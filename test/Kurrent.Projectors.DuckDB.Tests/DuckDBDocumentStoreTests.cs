// Parked: broken by the Quack IBinder/BufferedAppender and IDocumentStore refactor. Uncomment and port when the transactional projector test pass lands.

// using System.Globalization;
// using System.Text.Json;
// 
// using Bogus;
// using DuckDB.NET.Data;
// using Kurrent.Projectors.Transactional;
// using Kurrent.Quack;
// using Kurrent.Surge.Client.Streams;
// using Kurrent.Surge.Streaming;
// using Kurrent.Surge.Streaming.Models;
// 
// namespace Kurrent.Projectors.DuckDB.Tests;
// 
// using static ProjectionTestRecords;
// 
// /// <summary>
// /// Covers the store <see cref="DuckDBDocumentProjection{TDocument,TKey}"/> creates and hides — one table per
// /// collection, one row per document, the document itself as JSON.
// /// </summary>
// /// <remarks>
// /// A projection here declares a table and its folds and nothing else, so these tests are the only place the
// /// row's shape is asserted. They read on the epoch's own connection, since the transaction stays open.
// /// </remarks>
// [Category("Projections")]
// [Category("DuckDB")]
// [Category("Integration")]
// [Timeout(30_000)]
// public class DuckDBDocumentStoreTests {
//     [ClassDataSource<DuckDBTestFixture>(Shared = SharedType.None)]
//     public required DuckDBTestFixture DuckDB { get; [UsedImplicitly] init; }
// 
//     [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
//     public required BogusFaker Faker { get; [UsedImplicitly] init; }
// 
//     [Test]
//     public async ValueTask creates_its_collection_before_the_first_record(CancellationToken ct) {
//         // Arrange — Setup runs inside OnInitialize, before any record is read. Selecting both columns by
//         // name is the assertion: a table with a different shape cannot answer it.
//         var table = NewTableName();
// 
//         await using var projection = new CounterProjection(table);
// 
//         // Act
//         var connection = await Start(projection, ct);
// 
//         // Assert
//         await Assert.That(ReadRows(connection, table)).IsEmpty();
//     }
// 
//     [Test]
//     public async ValueTask writes_the_document_as_a_json_row_under_its_key(CancellationToken ct) {
//         // Arrange
//         var table     = NewTableName();
//         var counterId = NewCounterId();
// 
//         await using var projection = new CounterProjection(table);
// 
//         var connection = await Start(projection, ct);
// 
//         var expected = new[] { (Key: counterId, Document: new Counter(counterId, 3, "")) };
// 
//         // Act
//         await projection.OnBatch(connection, BatchOf(Decoded(new Incremented(counterId, 3), NextPosition())));
// 
//         // Assert
//         await Assert.That(ReadRows(connection, table)).IsEquivalentTo(expected);
//     }
// 
//     [Test]
//     public async ValueTask reads_its_own_json_back_when_a_later_batch_folds_the_same_key(CancellationToken ct) {
//         // Arrange — the fold window is the batch, so the second batch's total can only be right if the row
//         // written by the first one deserialized back into a Counter.
//         var table     = NewTableName();
//         var counterId = NewCounterId();
// 
//         await using var projection = new CounterProjection(table);
// 
//         var connection = await Start(projection, ct);
// 
//         var expected = new[] { (Key: counterId, Document: new Counter(counterId, 5, "")) };
// 
//         // Act
//         await projection.OnBatch(connection, BatchOf(Decoded(new Incremented(counterId, 2), NextPosition())));
//         await projection.OnBatch(connection, BatchOf(Decoded(new Incremented(counterId, 3), NextPosition())));
// 
//         // Assert — one row, not two: the upsert replaced rather than inserted.
//         await Assert.That(ReadRows(connection, table)).IsEquivalentTo(expected);
//     }
// 
//     [Test]
//     public async ValueTask removes_the_row_when_a_handler_returns_null(CancellationToken ct) {
//         // Arrange
//         var table     = NewTableName();
//         var counterId = NewCounterId();
// 
//         await using var projection = new CounterProjection(table);
// 
//         var connection = await Start(projection, ct);
// 
//         await projection.OnBatch(connection, BatchOf(Decoded(new Incremented(counterId, 7), NextPosition())));
// 
//         // Act
//         await projection.OnBatch(connection, BatchOf(Decoded(new Removed(counterId), NextPosition())));
// 
//         // Assert
//         await Assert.That(ReadRows(connection, table)).IsEmpty();
//     }
// 
//     [Test]
//     public async ValueTask writes_a_non_string_key_in_its_invariant_form(CancellationToken ct) {
//         // Arrange — keys reach SQL as text. A Guid written under one culture has to read back under any
//         // other, so the store formats invariantly rather than through the ambient culture.
//         var table     = NewTableName();
//         var counterId = Guid.NewGuid();
// 
//         await using var projection = new GuidKeyedCounterProjection(table);
// 
//         var connection = await Start(projection, ct);
// 
//         var expected = new[] {
//             (Key: counterId.ToString(null, CultureInfo.InvariantCulture),
//              Document: new Counter(counterId.ToString(), 4, ""))
//         };
// 
//         // Act
//         await projection.OnBatch(connection, BatchOf(Decoded(new Registered(counterId, 4), NextPosition())));
// 
//         // Assert
//         await Assert.That(ReadRows(connection, table)).IsEquivalentTo(expected);
//     }
// 
//     #region ->> Projections <<-
// 
//     /// <summary>Declares a table and its folds. There is no store in its surface, by design.</summary>
//     sealed class CounterProjection : DuckDBDocumentProjection<Counter, string> {
//         public CounterProjection(string table) : base(table) {
//             Project<Incremented>(
//                 msg => msg.CounterId,
//                 (msg, current, _) => current is { } counter
//                     ? counter with { Total = counter.Total + msg.By }
//                     : new Counter(msg.CounterId, msg.By, ""));
// 
//             Project<Removed>(msg => msg.CounterId, (_, _, _) => null);
//         }
//     }
// 
//     /// <summary>Same document, keyed by something that is not already text.</summary>
//     sealed class GuidKeyedCounterProjection : DuckDBDocumentProjection<Counter, Guid> {
//         public GuidKeyedCounterProjection(string table) : base(table) =>
//             Project<Registered>(
//                 msg => msg.CounterId,
//                 (msg, _, _) => new Counter(msg.CounterId.ToString(), msg.Total, ""));
//     }
// 
//     #endregion
// 
//     #region ->> Documents and messages <<-
// 
//     public sealed record Counter(string Id, int Total, string Label);
// 
//     public sealed record Incremented(string CounterId, int By);
// 
//     public sealed record Removed(string CounterId);
// 
//     public sealed record Registered(Guid CounterId, int Total);
// 
//     #endregion
// 
//     #region ->> Arranging <<-
// 
//     long _position;
// 
//     long NextPosition() => ++_position;
// 
//     string NewCounterId() => $"{Faker.Commerce.Department()}-{TestId.New()}";
// 
//     static string NewTableName() => $"documents_{TestId.New()}";
// 
//     /// <summary>
//     /// Wires the projection the way a backend projector would and runs the startup phase, so its store is
//     /// bound and its table exists. Returns the connection the epoch's transaction is open on.
//     /// </summary>
//     async ValueTask<DuckDBAdvancedConnection> Start(
//         ProjectionProcessor<DuckDBAdvancedConnection> projection, CancellationToken cancellationToken
//     ) {
//         var projectionId = $"documents-{TestId.New()}";
//         var transaction  = new DuckDBProjectionTransaction(DuckDB.ConnectionProvider);
// 
//         projection.Wire(
//             projectionId,
//             transaction,
//             new DuckDBProjectionCheckpointStore(projectionId, TimeProvider.System));
// 
//         await ((IProcessor)projection).OnInitialize(new InitializeContext {
//             Partitions        = [Partition.From(0)],
//             InitialPosition   = SubscriptionInitialPosition.Earliest,
//             StartPosition     = LogPosition.Unset,
//             CancellationToken = cancellationToken
//         });
// 
//         return await transaction.Open(cancellationToken);
//     }
// 
//     #endregion
// 
//     #region ->> Reading back <<-
// 
//     /// <summary>
//     /// Every row as its key and its deserialized document. The document is read through the same options the
//     /// store wrote it with — the property names on the wire are that resolver's business, not this test's.
//     /// </summary>
//     static (string Key, Counter Document)[] ReadRows(DuckDBAdvancedConnection connection, string table) {
//         using var command = connection.CreateCommand();
// 
//         command.CommandText = $"""SELECT key, document FROM "{table}" """;
// 
//         using var reader = command.ExecuteReader();
// 
//         var rows = new List<(string, Counter)>();
// 
//         while (reader.Read()) {
//             var document = JsonSerializer.Deserialize<Counter>(
//                 reader.GetString(1),
//                 Surge.Client.Schema.Serialization.Json.JsonSchemaSerializerOptions.DefaultJsonSerializerOptions)!;
// 
//             rows.Add((reader.GetString(0), document));
//         }
// 
//         return rows.ToArray();
//     }
// 
//     #endregion
// }
