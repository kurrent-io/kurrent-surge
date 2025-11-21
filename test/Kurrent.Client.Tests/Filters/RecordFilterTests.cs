using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class RecordFilterTests {
    [Test]
    public void none_returns_empty_filter() {
        var filter = RecordFilter.None;

        filter.ShouldBeOfType<EmptyFilter>();
    }

    [Test]
    public void by_stream_returns_stream_filter() {
        var filter = RecordFilter.ByStream("orders");

        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void by_stream_sets_target_stream() {
        var filter = RecordFilter.ByStream("orders");

        var streamFilter = filter.ShouldBeOfType<StreamFilter>();
        streamFilter.TargetStream.ShouldBe((StreamName)"orders");
    }

    [Test]
    public void by_stream_prefix_returns_stream_filter() {
        var filter = RecordFilter.ByStreamPrefix("order", "payment");

        filter.ShouldBeOfType<StreamFilter>();
    }

    [Test]
    public void by_stream_prefix_throws_on_empty_array() {
        Should.Throw<ArgumentException>(() => RecordFilter.ByStreamPrefix());
    }

    [Test]
    public void by_schema_returns_schema_filter() {
        var filter = RecordFilter.BySchema("Order");

        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void by_schema_prefix_returns_schema_filter() {
        var filter = RecordFilter.BySchemaPrefix("Order", "Payment");

        filter.ShouldBeOfType<SchemaFilter>();
    }

    [Test]
    public void by_schema_prefix_throws_on_empty_array() {
        Should.Throw<ArgumentException>(() => RecordFilter.BySchemaPrefix());
    }

    [Test]
    public void by_expression_returns_expression_filter() {
        var filter = RecordFilter.ByExpression("stream = 'orders'");

        filter.ShouldBeOfType<ExpressionFilter>();
    }
}
