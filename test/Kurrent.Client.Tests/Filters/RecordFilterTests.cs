using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class RecordFilterTests {
    [Test]
    public async Task none_returns_empty_filter() {
        var filter = RecordFilter.None;
        await Assert.That(filter).IsTypeOf<EmptyFilter>();
    }

    [Test]
    public async Task by_stream_returns_stream_filter() {
        var filter = RecordFilter.ByStream("orders");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task by_stream_sets_target_stream_for_single_stream() {
        var filter = RecordFilter.ByStream("orders");
        var streamFilter = (StreamFilter)filter;
        await Assert.That(streamFilter.TargetStream).IsEqualTo((StreamName)"orders");
    }

    [Test]
    public async Task by_stream_does_not_set_target_stream_for_multiple_streams() {
        var filter = RecordFilter.ByStream("orders", "payments");
        var streamFilter = (StreamFilter)filter;
        await Assert.That(streamFilter.TargetStream).IsEqualTo(StreamName.All);
    }

    [Test]
    public async Task by_stream_throws_on_empty_array() {
        await Assert.That(() => RecordFilter.ByStream()).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task by_stream_prefix_returns_stream_filter() {
        var filter = RecordFilter.ByStreamPrefix("order", "payment");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task by_stream_prefix_throws_on_empty_array() {
        await Assert.That(() => RecordFilter.ByStreamPrefix()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_schema_returns_schema_filter() {
        var filter = RecordFilter.BySchema("Order");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task by_schema_with_multiple_schemas_returns_schema_filter() {
        var filter = RecordFilter.BySchema("Order", "Payment");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task by_schema_throws_on_empty_array() {
        await Assert.That(() => RecordFilter.BySchema()).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task by_schema_prefix_returns_schema_filter() {
        var filter = RecordFilter.BySchemaPrefix("Order", "Payment");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task by_schema_prefix_throws_on_empty_array() {
        await Assert.That(() => RecordFilter.BySchemaPrefix()).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_category_returns_stream_filter_targeting_idx_ce() {
        var filter = RecordFilter.ByCategory("orders");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-ce-orders"));
    }

    [Test]
    public async Task by_category_multiple_returns_stream_filter_with_alternation() {
        var filter = RecordFilter.ByCategory("orders", "payments");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).Contains("\\$idx-ce-orders");
        await Assert.That(filter.Expression).Contains("\\$idx-ce-payments");
    }

    [Test]
    public async Task by_category_throws_on_empty_array() {
        await Assert.That(() => RecordFilter.ByCategory()).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task by_expression_returns_expression_filter() {
        var filter = RecordFilter.ByExpression("stream = 'orders'");

        await Assert.That(filter).IsTypeOf<ExpressionFilter>();
    }
}
