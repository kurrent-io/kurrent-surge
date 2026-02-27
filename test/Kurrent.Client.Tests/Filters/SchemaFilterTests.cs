using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class SchemaFilterTests {
    readonly RecordFaker _faker = new();

    [Test]
    public async Task by_schema_prefix_matches_schema_prefixes() {
        var filter          = RecordFilter.BySchemaPrefix("Order", "Payment");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentReceived");
        var nonMatching     = _faker.With(schemaName: "UserRegistered");

        await Assert.That(filter.IsMatch(matchingOrder)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayment)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task by_schema_prefix_with_multiple_prefixes() {
        var filter          = RecordFilter.BySchemaPrefix("Order", "Payment", "Refund");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentProcessed");
        var matchingRefund  = _faker.With(schemaName: "RefundIssued");
        var nonMatching     = _faker.With(schemaName: "UserEvent");

        await Assert.That(filter.IsMatch(matchingOrder)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayment)).IsTrue();
        await Assert.That(filter.IsMatch(matchingRefund)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }
}
