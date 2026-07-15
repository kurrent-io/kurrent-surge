// using Kurrent.Surge.Client.Streams;
//
// namespace Kurrent.Surge.Tests.Streams;
//
// [Category("Streams")]
// public class StreamsClientQueryTests {
//     // Extension methods don't dereference the client during construction,
//     // so null is safe for verifying return types.
//     static readonly StreamsClient Client = null!;
//
//     [Test]
//     public async Task query_returns_log_query_builder() {
//         var builder = Client.Query();
//
//         await Assert.That(builder).IsTypeOf<LogQueryBuilder>();
//     }
//
//     [Test]
//     public async Task query_with_stream_name_returns_stream_query_builder() {
//         var builder = Client.Query(StreamName.From("test-stream"));
//
//         await Assert.That(builder).IsTypeOf<StreamQueryBuilder>();
//     }
//
//     [Test]
//     public async Task query_with_string_returns_stream_query_builder() {
//         var builder = Client.Query("test-stream");
//
//         await Assert.That(builder).IsTypeOf<StreamQueryBuilder>();
//     }
// }
