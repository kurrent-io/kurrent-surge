using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class SchemaFilterTests {
    readonly RecordFaker _faker = new();

    [Test]
    public async Task by_schema_matches_exact_schema_names() {
        var filter          = RecordFilter.BySchema("OrderCreated", "PaymentReceived");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentReceived");
        var nonMatching     = _faker.With(schemaName: "UserRegistered");

        await Assert.That(filter.IsMatch(matchingOrder)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayment)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task by_schema_pattern_matches_prefix() {
        var filter          = RecordFilter.BySchemaPattern("^Order.*$");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "OrderUpdated");
        var nonMatching     = _faker.With(schemaName: "UserEvent");

        await Assert.That(filter.IsMatch(matchingOrder)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayment)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }
}
