using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Filters;

[Category("Filters")]
public class RecordFilterTests {
    [Test]
    public async ValueTask none_returns_empty_filter() {
        var filter = RecordFilter.None;
        await Assert.That(filter).IsTypeOf<EmptyFilter>();
    }

    [Test]
    public async ValueTask by_stream_returns_stream_name_filter() {
        var filter = (StreamNameFilter)RecordFilter.ByStream("orders");

        await Assert.That(filter.TargetStream).IsEqualTo((StreamName)"orders");
    }

    [Test]
    public async ValueTask by_category_returns_category_filter() {
        var filter = (CategoryFilter)RecordFilter.ByCategory("orders");

        await Assert.That(filter.CategoryName).IsEqualTo("orders");
    }

    [Test]
    public async ValueTask by_schema_returns_schema_filter() {
        var filter = (SchemaFilter)RecordFilter.BySchema("OrderCreated");

        await Assert.That(filter.SchemaName).IsEqualTo("OrderCreated");
    }

    [Test]
    public async ValueTask by_user_index_returns_user_index_filter() {
        var filter = (UserIndexFilter)RecordFilter.ByUserIndex("my-index");

        await Assert.That(filter.IndexName).IsEqualTo("$idx-user-my-index");
    }

    [Test]
    public async ValueTask by_stream_pattern_returns_stream_regex_filter() {
        var filter = RecordFilter.ByStreamPattern("^Orders-.*$");

        await Assert.That(filter).IsTypeOf<StreamRegexFilter>();
    }

    [Test]
    public async ValueTask by_expression_returns_expression_filter() {
        var filter = RecordFilter.ByExpression("stream = 'orders'");

        await Assert.That(filter).IsTypeOf<ExpressionFilter>();
    }

    [Test]
    public async ValueTask parse_returns_an_expression_filter() {
        // Arrange
        var expression = "stream = 'orders'";

        // Act
        var filter = RecordFilter.Parse(expression, null);

        // Assert
        await Assert.That(filter).IsTypeOf<ExpressionFilter>();
        await Assert.That(filter.Expression).IsEqualTo(expression);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async ValueTask parse_rejects_an_empty_expression(string expression) {
        // Arrange — the raw log is None, asked for by name; a blank value must not select it.
        var parse = () => RecordFilter.Parse(expression, null);

        // Act & Assert
        await Assert.That(parse).Throws<FormatException>();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async ValueTask try_parse_returns_false_for_an_empty_expression(string? expression) {
        // Act
        var parsed = RecordFilter.TryParse(expression, null, out var filter);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(filter).IsNull();
    }
}
