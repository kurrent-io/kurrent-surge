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
    public async Task by_stream_throws_on_null_or_whitespace() {
        await Assert.That(() => RecordFilter.ByStream("")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByStream(" ")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_stream_throws_on_index_stream_name() {
        await Assert.That(() => RecordFilter.ByStream("$idx-ce-orders")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByStream("$idx-et-OrderCreated")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByStream("$idx-user-MyIndex")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_schema_returns_stream_filter_targeting_idx_et() {
        var filter = RecordFilter.BySchema("OrderCreated");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-et-OrderCreated"));
    }

    [Test]
    public async Task by_schema_throws_on_null_or_whitespace() {
        await Assert.That(() => RecordFilter.BySchema("")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.BySchema(" ")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_category_returns_stream_filter_targeting_idx_ce() {
        var filter = RecordFilter.ByCategory("orders");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-ce-orders"));
    }

    [Test]
    public async Task by_category_throws_on_null_or_whitespace() {
        await Assert.That(() => RecordFilter.ByCategory("")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByCategory(" ")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_user_index_returns_stream_filter_targeting_idx_user() {
        var filter = RecordFilter.ByUserIndex("MyIndex");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-user-MyIndex"));
    }

    [Test]
    public async Task by_user_index_throws_on_null_or_whitespace() {
        await Assert.That(() => RecordFilter.ByUserIndex("")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByUserIndex(" ")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_stream_pattern_throws_on_index_target() {
        await Assert.That(() => RecordFilter.ByStreamPattern(@"^\$idx-ce-orders$")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => RecordFilter.ByStreamPattern(@"^\$idx-et-.+$")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task by_expression_returns_expression_filter() {
        var filter = RecordFilter.ByExpression("stream = 'orders'");

        await Assert.That(filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async Task is_index_filter_true_for_category_schema_and_user_index() {
        await Assert.That(RecordFilter.ByCategory("orders").IsIndexFilter).IsTrue();
        await Assert.That(RecordFilter.BySchema("OrderCreated").IsIndexFilter).IsTrue();
        await Assert.That(RecordFilter.ByUserIndex("MyIndex").IsIndexFilter).IsTrue();
    }

    [Test]
    public async Task is_index_filter_false_for_regular_stream() {
        await Assert.That(RecordFilter.ByStream("orders-123").IsIndexFilter).IsFalse();
        await Assert.That(RecordFilter.None.IsIndexFilter).IsFalse();
    }
}
