using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

// Binding reads a column's value. Parsing builds a predicate over it. A declared text form belongs to
// the first and must not reach the second.
[Category("RecordFilters")]
public class ColumnBindingTests {
    #region ->> Fixtures <<-

    static readonly FilterSchema<Order> Bound = FilterSchema<Order>.Create(
        schema => schema
            .Scalar(o => o.Stream,                  "stream")
            .Scalar(o => o.Revision,                "revision")
            .Scalar(o => o.Status, Shout,           "status")
            .Map(ResolveTag,                        "tags", "props")
    );

    static string? Shout(string? status) => status?.ToUpperInvariant();

    static object? ResolveTag(Order order, string? key) =>
        key is not null && order.Tags.TryGetValue(key, out var value) ? value : null;

    static Order NewOrder() =>
        new() {
            Stream    = "orders-1",
            Revision  = 7,
            Amount    = 150m,
            CreatedAt = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Status    = "shipped",
            Tags      = { ["region"] = "us-east" }
        };

    // A key derived from a column must not depend on the type the value arrived as, so the default
    // text form pins a format per type rather than deferring to ToString.
    sealed record Typed(Guid Id, DateTime At);

    static readonly FilterSchema<Typed> TypedColumns = FilterSchema<Typed>.Create(
        schema => schema
            .Scalar(t => t.Id, "id")
            .Scalar(t => t.At, "at")
    );

    public sealed class UndeclaredColumnCases : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() {
            yield return new("nope = 'x'",                     DisplayName: "comparison");
            yield return new("nope IS NULL",                   DisplayName: "is null");
            yield return new("nope LIKE 'x%'",                 DisplayName: "like");
            yield return new("nope IN ('x')",                  DisplayName: "in");
            yield return new("nope BETWEEN 1 AND 2",           DisplayName: "between");
            yield return new("NOT (nope = 'x')",               DisplayName: "under negation");
            yield return new("stream = 'a' AND nope = 'x'",    DisplayName: "right of AND");
            yield return new("nope = 'x' OR stream = 'a'",     DisplayName: "left of OR");
            yield return new("nope['region'] = 'x'",           DisplayName: "with a key");
        }
    }

    #endregion

    #region ->> Binding <<-

    [Test]
    public async ValueTask binds_a_string_column() {
        // Arrange
        var bound = Bound.TryBind("stream", out var read);

        // Act
        var value = read!(NewOrder());

        // Assert
        await Assert.That(bound).IsTrue();
        await Assert.That(value).IsEqualTo("orders-1");
    }

    [Test]
    public async ValueTask binds_a_non_string_column_through_to_string() {
        // Arrange
        Bound.TryBind("revision", out var read);

        // Act
        var value = read!(NewOrder());

        // Assert
        await Assert.That(value).IsEqualTo("7");
    }

    [Test]
    public async ValueTask binds_a_column_through_its_declared_text_form() {
        // Arrange
        Bound.TryBind("status", out var read);

        // Act
        var value = read!(NewOrder());

        // Assert
        await Assert.That(value).IsEqualTo("SHIPPED");
    }

    [Test]
    public async ValueTask binds_a_map_key_in_bracket_dot_and_arrow_notation() {
        // Arrange
        var expected = "us-east";
        var order    = NewOrder();

        // Act
        Bound.TryBind("props['region']", out var bracket);
        Bound.TryBind("props.region",    out var dot);
        Bound.TryBind("props->>'region'", out var arrow);

        // Assert
        await Assert.That(bracket!(order)).IsEqualTo(expected);
        await Assert.That(dot!(order)).IsEqualTo(expected);
        await Assert.That(arrow!(order)).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask binds_a_guid_column_without_dashes() {
        // Arrange — a hash key must read the same whether the id arrived as a Guid or as its text
        var id       = Guid.Parse("d0332eb7-4806-43e4-81bf-3205296958fd");
        var expected = "d0332eb7480643e481bf3205296958fd";

        TypedColumns.TryBind("id", out var read);

        // Act
        var value = read!(new(id, DateTime.UnixEpoch));

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask binds_a_date_time_column_in_round_trip_form() {
        // Arrange — round-trip form, so the key does not shift with the machine's culture
        var at       = new DateTime(2026, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var expected = "2026-06-15T10:30:00.0000000Z";

        TypedColumns.TryBind("at", out var read);

        // Act
        var value = read!(new(Guid.Empty, at));

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask reads_null_for_a_missing_map_key() {
        // Arrange
        Bound.TryBind("props['nope']", out var read);

        // Act
        var value = read!(NewOrder());

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    public async ValueTask refuses_to_bind_an_undeclared_column() {
        // Arrange & Act
        var bound = Bound.TryBind("nonexistent", out var read);

        // Assert
        await Assert.That(bound).IsFalse();
        await Assert.That(read).IsNull();
    }

    [Test]
    public async ValueTask refuses_to_bind_a_malformed_path() {
        // Arrange & Act
        var bound = Bound.TryBind("!!!invalid", out var read);

        // Assert
        await Assert.That(bound).IsFalse();
        await Assert.That(read).IsNull();
    }

    #endregion

    #region ->> Separation <<-

    [Test]
    public async ValueTask a_declared_text_form_does_not_change_what_a_predicate_matches() {
        // Arrange — 'status' binds as SHIPPED, but the predicate still reads the stored value
        var order = NewOrder();

        // Act
        Bound.TryBind("status", out var read);
        var stored  = Bound.Parse("status = 'shipped'").IsMatch(order);
        var shouted = Bound.Parse("status = 'SHIPPED'").IsMatch(order);

        // Assert
        await Assert.That(read!(order)).IsEqualTo("SHIPPED");
        await Assert.That(stored).IsTrue();
        await Assert.That(shouted).IsFalse();
    }

    #endregion

    #region ->> Rejection <<-

    [Test]
    [UndeclaredColumnCases]
    public async ValueTask refuses_to_parse_an_undeclared_column(string expression) {
        // Arrange
        const string expectedPrefix = "Error parsing filter:";

        // Act
        var parsed = Bound.TryParse(expression, out var filter, out var error);

        // Assert
        await Assert.That(parsed).IsFalse();
        await Assert.That(filter).IsNull();
        await Assert.That(error!.ErrorMessage).StartsWith(expectedPrefix).And.Contains("nope");
    }

    [Test]
    public async ValueTask names_the_schema_target_in_the_rejection() {
        // Arrange
        var expression = "nope = 'x'";

        // Act
        Bound.TryParse(expression, out _, out var error);

        // Assert
        await Assert.That(error!.ErrorMessage).Contains("FilterSchema<Order>");
    }

    #endregion
}
