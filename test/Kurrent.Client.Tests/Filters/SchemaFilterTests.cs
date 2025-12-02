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

        filter.IsMatch(matchingOrder).ShouldBeTrue("Order schema should match");
        filter.IsMatch(matchingPayment).ShouldBeTrue("Payment schema should match");
        filter.IsMatch(nonMatching).ShouldBeFalse("Non-matching schema should not match");
    }

    [Test]
    public void by_schema_prefix_with_multiple_prefixes() {
        var filter          = RecordFilter.BySchemaPrefix("Order", "Payment", "Refund");
        var matchingOrder   = _faker.With(schemaName: "OrderCreated");
        var matchingPayment = _faker.With(schemaName: "PaymentProcessed");
        var matchingRefund  = _faker.With(schemaName: "RefundIssued");
        var nonMatching     = _faker.With(schemaName: "UserEvent");

        filter.IsMatch(matchingOrder).ShouldBeTrue("Order schema should match");
        filter.IsMatch(matchingPayment).ShouldBeTrue("Payment schema should match");
        filter.IsMatch(matchingRefund).ShouldBeTrue("Refund schema should match");
        filter.IsMatch(nonMatching).ShouldBeFalse("Non-matching schema should not match");
    }
}
