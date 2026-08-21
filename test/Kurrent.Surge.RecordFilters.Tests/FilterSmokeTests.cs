using System.Text.Json.Nodes;
using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

[Category("RecordFilters")]
public class FilterSmokeTests {
    static Order NewOrder(string stream = "orders-1", string status = "shipped", decimal amount = 150m) =>
        new() {
            Stream    = stream,
            Revision  = 7,
            Status    = status,
            Amount    = amount,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Tags      = { ["region"] = "us-east" },
            Payload   = JsonNode.Parse("""{"address":{"city":"lisbon"},"priority":3}""")
        };

    [Test]
    public async ValueTask evaluates_a_scalar_predicate() {
        // Arrange
        var filter = OrderSchema.Default.Parse("status = 'shipped'");

        // Act & Assert
        await Assert.That(filter.IsMatch(NewOrder())).IsTrue();
        await Assert.That(filter.IsMatch(NewOrder(status: "pending"))).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_a_numeric_predicate_without_boxing_path() {
        // Arrange
        var filter = OrderSchema.Default.Parse("amount > 100 AND revision BETWEEN 1 AND 10");

        // Act & Assert
        await Assert.That(filter.IsMatch(NewOrder())).IsTrue();
        await Assert.That(filter.IsMatch(NewOrder(amount: 50m))).IsFalse();
    }

    [Test]
    public async ValueTask evaluates_a_map_key_through_an_alias() {
        // Arrange — 'props' is an alias of the canonical 'tags' column
        var filter = OrderSchema.Default.Parse("props['region'] = 'us-east'");

        // Act
        var matched = filter.IsMatch(NewOrder());

        // Assert
        await Assert.That(matched).IsTrue();
    }

    [Test]
    public async ValueTask evaluates_a_nested_json_path() {
        // Arrange
        var filter = OrderSchema.Default.Parse("data.address.city = 'lisbon'");

        // Act
        var matched = filter.IsMatch(NewOrder());

        // Assert
        await Assert.That(matched).IsTrue();
    }

    [Test]
    public async ValueTask strips_the_configured_qualifier() {
        // Arrange
        var filter = OrderSchema.Default.Parse("order.stream = 'orders-1'");

        // Act
        var matched = filter.IsMatch(NewOrder());

        // Assert
        await Assert.That(matched).IsTrue();
    }

    [Test]
    public async ValueTask compares_strings_case_sensitively() {
        // Arrange
        var filter = OrderSchema.Default.Parse("status = 'SHIPPED'");

        // Act
        var matched = filter.IsMatch(NewOrder());

        // Assert
        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async ValueTask renders_aliases_as_their_canonical_column() {
        // Arrange
        var filter        = OrderSchema.Default.Parse("props['region'] = 'us-east'");
        var expectedSql   = "tags->>'region' = 'us-east'";

        // Act
        var sql = filter.ToSql();

        // Assert
        await Assert.That(sql).IsEqualTo(expectedSql);
    }

    [Test]
    public async ValueTask renders_a_json_path_in_arrow_notation() {
        // Arrange
        var filter      = OrderSchema.Default.Parse("data.address.city = 'lisbon'");
        var expectedSql = "payload->>'$.address.city' = 'lisbon'";

        // Act
        var sql = filter.ToSql();

        // Assert
        await Assert.That(sql).IsEqualTo(expectedSql);
    }

    [Test]
    public async ValueTask renders_every_alias_in_one_expression_to_its_canonical_column() {
        // Arrange
        var filter      = OrderSchema.Default.Parse("stream = 'orders-1' AND props['region'] = 'us-east' AND revision > 1");
        var expectedSql = "((stream = 'orders-1') AND (tags->>'region' = 'us-east')) AND (stream_revision > 1)";

        // Act
        var sql = filter.ToSql();

        // Assert
        await Assert.That(sql).IsEqualTo(expectedSql);
    }

    [Test]
    public async ValueTask reports_a_parse_error_instead_of_throwing() {
        // Arrange
        var expression = "stream = ";

        // Act
        var parsed = OrderSchema.Default.TryParse(expression, out _, out var error);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("end of input");
    }

    [Test]
    public async ValueTask refuses_to_render_an_undeclared_column() {
        // Arrange
        var filter = OrderSchema.Default.Parse("nonexistent = 'x'");

        // Act & Assert — evaluation reports no match; rendering names the mistake
        await Assert.That(filter.IsMatch(NewOrder())).IsFalse();
        await Assert.That(() => filter.ToSql()).Throws<InvalidOperationException>();
    }
}
