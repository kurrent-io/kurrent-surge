using Kurrent.Surge.RecordFilters.Tests.Targets;

namespace Kurrent.Surge.RecordFilters.Tests;

[Category("RecordFilters")]
public class ParameterTests {
    static readonly DateTimeOffset Noon = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async ValueTask binds_a_value_by_name() {
        // Arrange
        var parameters = new FilterParameters().Add("s", "orders-1");

        // Act
        var filter = OrderSchema.Default.Parse("stream = @s", parameters);

        // Assert
        await Assert.That(filter.ToSql()).IsEqualTo("stream = 'orders-1'");
    }

    // The template survives so a log can show the shape as written; ToSql carries the values.
    [Test]
    public async ValueTask keeps_the_template_as_written() {
        var filter = OrderSchema.Default.Parse("stream = @s", new FilterParameters().Add("s", "orders-1"));

        await Assert.That(filter.Expression).IsEqualTo("stream = @s");
    }

    [Test]
    public async ValueTask binds_the_same_name_more_than_once() {
        var filter = OrderSchema.Default.Parse(
            "stream = @s OR status = @s",
            new FilterParameters().Add("s", "shipped"));

        await Assert.That(filter.ToSql()).IsEqualTo("(stream = 'shipped') OR (status = 'shipped')");
    }

    [Test]
    public async ValueTask binds_every_supported_type() {
        // Arrange
        var parameters = new FilterParameters()
            .Add("text",   "orders-1")
            .Add("number", 42L)
            .Add("money",  12.5m)
            .Add("flag",   true)
            .Add("id",     Guid.Parse("4a000000-0000-0000-0000-000000000001"))
            .Add("bytes",  new byte[] { 0x4A, 0x01 })
            .Add("when",   Noon);

        // Act
        var filter = OrderSchema.Default.Parse(
            "stream = @text AND stream_revision = @number AND amount = @money AND created_at = @when",
            new FilterParameters()
                .Add("text",   "orders-1")
                .Add("number", 42L)
                .Add("money",  12.5m)
                .Add("when",   Noon));

        // Assert
        await Assert.That(parameters.Count).IsEqualTo(7);
        await Assert.That(filter.ToSql()).IsEqualTo(
            "(((stream = 'orders-1') AND (stream_revision = 42)) AND (amount = 12.5)) AND (created_at = 1781524800000)");
    }

    // A moment binds as Unix milliseconds because created_at holds milliseconds.
    [Test]
    public async ValueTask binds_a_moment_as_milliseconds() {
        var filter = OrderSchema.Default.Parse("created_at > @since", new FilterParameters().Add("since", Noon));

        await Assert.That(filter.ToSql()).IsEqualTo($"created_at > {Noon.ToUnixTimeMilliseconds()}");
    }

    [Test]
    public async ValueTask binds_bytes_as_a_blob_call() {
        var filter = OrderSchema.Default.Parse(
            "record_id = @id",
            new FilterParameters().Add("id", new byte[] { 0x4A, 0x00, 0x01 }));

        await Assert.That(filter.ToSql()).IsEqualTo("record_id = from_hex('4A0001')");
    }

    // A value never becomes text in the expression, so an apostrophe cannot end the literal early and
    // the rest cannot be read as SQL.
    [Test]
    public async ValueTask a_value_cannot_change_the_shape_of_the_expression() {
        // Arrange
        var parameters = new FilterParameters().Add("s", "o' OR 1=1 --");

        // Act
        var filter = OrderSchema.Default.Parse("stream = @s", parameters);

        // Assert
        await Assert.That(filter.ToSql()).IsEqualTo("stream = 'o'' OR 1=1 --'");
        await Assert.That(filter.IsMatch(Order("orders-1"))).IsFalse();
    }

    [Test]
    public async ValueTask rejects_an_unbound_parameter() {
        var parsed = OrderSchema.Default.TryParse("stream = @missing", new FilterParameters(), out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("no value is bound to parameter \"@missing\"");
    }

    [Test]
    public async ValueTask rejects_a_parameter_the_expression_never_names() {
        var parameters = new FilterParameters().Add("s", "orders-1").Add("unused", 1L);

        var parsed = OrderSchema.Default.TryParse("stream = @s", parameters, out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("\"@unused\" is bound but never used");
    }

    [Test]
    public async ValueTask rejects_binding_the_same_name_twice() {
        var bind = () => new FilterParameters().Add("s", "a").Add("s", "b");

        await Assert.That(bind).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask accepts_the_marker_when_binding() {
        var filter = OrderSchema.Default.Parse("stream = @s", new FilterParameters().Add("@s", "orders-1"));

        await Assert.That(filter.ToSql()).IsEqualTo("stream = 'orders-1'");
    }

    [Test]
    public async ValueTask names_are_case_insensitive() {
        var filter = OrderSchema.Default.Parse("stream = @Stream", new FilterParameters().Add("stream", "orders-1"));

        await Assert.That(filter.ToSql()).IsEqualTo("stream = 'orders-1'");
    }

    // A parameter is a literal, so the same type rules apply to it as to one written inline.
    [Test]
    public async ValueTask a_bound_value_obeys_the_type_rules() {
        var parsed = OrderSchema.Default.TryParse("stream = @n", new FilterParameters().Add("n", 5L), out _, out var error);

        await Assert.That(parsed).IsFalse();
        await Assert.That(error!.ErrorMessage).Contains("an explicit cast is required");
    }

    [Test]
    public async ValueTask evaluates_against_a_record() {
        var filter = OrderSchema.Default.Parse(
            "stream = @s AND stream_revision > @from",
            new FilterParameters().Add("s", "orders-1").Add("from", 2L));

        await Assert.That(filter.IsMatch(Order("orders-1", 3))).IsTrue();
        await Assert.That(filter.IsMatch(Order("orders-1", 1))).IsFalse();
        await Assert.That(filter.IsMatch(Order("orders-2", 3))).IsFalse();
    }

    static Order Order(string stream, long revision = 1) =>
        new() { Stream = stream, Revision = revision, Amount = 0m, CreatedAt = Noon };
}
