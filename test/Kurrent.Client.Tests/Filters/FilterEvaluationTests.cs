// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Tests.Filters;
//
// [Category("Filters"), Skip("Filter tests are currently disabled pending implementation of the full filter evaluation engine.")]
// public class FilterEvaluationTests {
//     readonly TestRecordAccessor _accessor = new();
//
//     [Test]
//     public void evaluates_stream_equality_correctly() {
//         var filter = FilterExpression.Create("stream = 'orders'").Value;
//         var record = new TestRecord { Stream = "orders" };
//
//         var result = filter.Evaluate(record, _accessor);
//
//         result.ShouldBeTrue();
//     }
//
//     [Test]
//     public void evaluates_stream_inequality_correctly() {
//         var filter = FilterExpression.Create("stream != 'test'").Value;
//         var record = new TestRecord { Stream = "orders" };
//
//         var result = filter.Evaluate(record, _accessor);
//
//         result.ShouldBeTrue();
//     }
//
//     [Test]
//     public void evaluates_contains_expressions_case_insensitive() {
//         var filter = FilterExpression.Create("stream ~ 'ord'").Value;
//         var record = new TestRecord { Stream = "ORDERS" };
//
//         var result = filter.Evaluate(record, _accessor);
//
//         result.ShouldBeTrue();
//     }
//
//     [Test]
//     public void evaluates_contains_exact_expressions_case_sensitive() {
//         var filter        = FilterExpression.Create("stream ~* 'ORD'").Value;
//         var recordMatch   = new TestRecord { Stream = "ORDERS" };
//         var recordNoMatch = new TestRecord { Stream = "orders" };
//
//         filter.Evaluate(recordMatch, _accessor).ShouldBeTrue();
//         filter.Evaluate(recordNoMatch, _accessor).ShouldBeFalse();
//     }
//
//     [Test]
//     public void evaluates_not_contains_expressions() {
//         var filter = FilterExpression.Create("stream !~ 'test'").Value;
//         var record = new TestRecord { Stream = "orders" };
//
//         var result = filter.Evaluate(record, _accessor);
//
//         result.ShouldBeTrue();
//     }
//
//     [Test]
//     public void evaluates_in_expressions() {
//         var filter            = FilterExpression.Create("stream IN ('orders', 'payments')").Value;
//         var matchingRecord    = new TestRecord { Stream = "orders" };
//         var nonMatchingRecord = new TestRecord { Stream = "users" };
//
//         filter.Evaluate(matchingRecord, _accessor).ShouldBeTrue();
//         filter.Evaluate(nonMatchingRecord, _accessor).ShouldBeFalse();
//     }
//
//     [Test]
//     public void evaluates_not_in_expressions() {
//         var filter            = FilterExpression.Create("stream NOT IN ('test', 'system')").Value;
//         var matchingRecord    = new TestRecord { Stream = "orders" };
//         var nonMatchingRecord = new TestRecord { Stream = "test" };
//
//         filter.Evaluate(matchingRecord, _accessor).ShouldBeTrue();
//         filter.Evaluate(nonMatchingRecord, _accessor).ShouldBeFalse();
//     }
//
//     [Test]
//     public void evaluates_null_checks() {
//         var nullFilter    = FilterExpression.Create("metadata.userId IS NULL").Value;
//         var notNullFilter = FilterExpression.Create("metadata.userId IS NOT NULL").Value;
//
//         var recordWithNull = new TestRecord();
//         var recordWithValue = new TestRecord {
//             Metadata = new Dictionary<string, object?> { ["metadata.userId"] = "user123" }
//         };
//
//         nullFilter.Evaluate(recordWithNull, _accessor).ShouldBeTrue();
//         nullFilter.Evaluate(recordWithValue, _accessor).ShouldBeFalse();
//
//         notNullFilter.Evaluate(recordWithNull, _accessor).ShouldBeFalse();
//         notNullFilter.Evaluate(recordWithValue, _accessor).ShouldBeTrue();
//     }
//
//     [Test]
//     public void evaluates_logical_and_expressions() {
//         var filter = FilterExpression.Create("stream = 'orders' AND schema.name = 'OrderCreated'").Value;
//         var matchingRecord = new TestRecord {
//             Stream     = "orders",
//             SchemaName = "OrderCreated"
//         };
//
//         var nonMatchingRecord = new TestRecord {
//             Stream     = "orders",
//             SchemaName = "OrderUpdated"
//         };
//
//         filter.Evaluate(matchingRecord, _accessor).ShouldBeTrue();
//         filter.Evaluate(nonMatchingRecord, _accessor).ShouldBeFalse();
//     }
//
//     [Test]
//     public void evaluates_logical_or_expressions() {
//         var filter         = FilterExpression.Create("stream = 'orders' OR stream = 'payments'").Value;
//         var ordersRecord   = new TestRecord { Stream = "orders" };
//         var paymentsRecord = new TestRecord { Stream = "payments" };
//         var usersRecord    = new TestRecord { Stream = "users" };
//
//         filter.Evaluate(ordersRecord, _accessor).ShouldBeTrue();
//         filter.Evaluate(paymentsRecord, _accessor).ShouldBeTrue();
//         filter.Evaluate(usersRecord, _accessor).ShouldBeFalse();
//     }
//
//     [Test]
//     public void evaluates_comparison_operators() {
//         var greaterFilter = FilterExpression.Create("metadata.amount > 100").Value;
//         var lessFilter    = FilterExpression.Create("metadata.amount < 50").Value;
//
//         var highValueRecord = new TestRecord {
//             Metadata = new Dictionary<string, object?> { ["metadata.amount"] = 150 }
//         };
//
//         var lowValueRecord = new TestRecord {
//             Metadata = new Dictionary<string, object?> { ["metadata.amount"] = 25 }
//         };
//
//         greaterFilter.Evaluate(highValueRecord, _accessor).ShouldBeTrue();
//         greaterFilter.Evaluate(lowValueRecord, _accessor).ShouldBeFalse();
//
//         lessFilter.Evaluate(highValueRecord, _accessor).ShouldBeFalse();
//         lessFilter.Evaluate(lowValueRecord, _accessor).ShouldBeTrue();
//     }
// }
//
// public class TestRecord {
//     public string                      Stream        { get; set; } = string.Empty;
//     public string                      SchemaName    { get; set; } = string.Empty;
//     public string                      SchemaVersion { get; set; } = string.Empty;
//     public DateTime                    Timestamp     { get; set; } = DateTime.UtcNow;
//     public Dictionary<string, object?> Metadata      { get; set; } = new();
// }
//
// public class TestRecordAccessor : IRecordAccessor<TestRecord> {
//     public object? GetPropertyValue(TestRecord record, string propertyName) {
//         return propertyName.ToLowerInvariant() switch {
//             "stream"         => record.Stream,
//             "schema.name"    => record.SchemaName,
//             "schema.version" => record.SchemaVersion,
//             "timestamp"      => record.Timestamp,
//             _ when propertyName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase) =>
//                 TryGetMetadata(record, propertyName, out var value) ? value : null,
//             _ => null
//         };
//     }
//
//     public IReadOnlyDictionary<string, object?> GetMetadata(TestRecord record) => record.Metadata;
//
//     public ReadOnlySpan<char> GetStream(TestRecord record) => record.Stream.AsSpan();
//
//     public ReadOnlySpan<char> GetSchema(TestRecord record) => record.SchemaName.AsSpan();
//
//     public ReadOnlySpan<char> GetSchemaName(TestRecord record) => record.SchemaName.AsSpan();
//
//     public ReadOnlySpan<char> GetSchemaVersion(TestRecord record) => record.SchemaVersion.AsSpan();
//
//     public ReadOnlySpan<char> GetSchemaDataFormat(TestRecord record) => "json".AsSpan();
//
//     public DateTime GetTimestamp(TestRecord record) => record.Timestamp;
//
//     public bool TryGetMetadata(TestRecord record, string key, out object? value) {
//         return record.Metadata.TryGetValue(key, out value);
//     }
// }
