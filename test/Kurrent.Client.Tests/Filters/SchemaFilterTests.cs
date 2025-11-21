using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class SchemaFilterTests {
    readonly RecordFaker _faker = new();

    [Test]
    public void by_schema_prefix_matches_schema_prefixes() {
        var filter          = RecordFilter.BySchemaPrefix("Order", "Payment");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentReceived");
        var nonMatching     = _faker.With(schemaName: "UserRegistered");

        filter.IsMatch(matchingOrder).ShouldBeTrue();
        filter.IsMatch(matchingPayment).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void by_schema_prefix_with_multiple_prefixes() {
        var filter          = RecordFilter.BySchemaPrefix("Order", "Payment", "Refund");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentProcessed");
        var matchingRefund  = _faker.With(schemaName: "RefundIssued");
        var nonMatching     = _faker.With(schemaName: "UserEvent");

        filter.IsMatch(matchingOrder).ShouldBeTrue();
        filter.IsMatch(matchingPayment).ShouldBeTrue();
        filter.IsMatch(matchingRefund).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }
}
