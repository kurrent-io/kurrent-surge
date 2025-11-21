using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class StreamFilterTests {
    readonly RecordFaker _faker = new();

    [Test]
    public void by_stream_matches_exact_stream() {
        var filter      = RecordFilter.ByStream("orders");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("orders-123");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void by_stream_prefix_matches_stream_prefixes() {
        var filter           = RecordFilter.ByStreamPrefix("order", "payment");
        var matchingOrders   = _faker.With("order-123");
        var matchingPayments = _faker.With("payment-456");
        var nonMatching      = _faker.With("user-789");

        filter.IsMatch(matchingOrders).ShouldBeTrue();
        filter.IsMatch(matchingPayments).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void by_stream_prefix_with_single_prefix() {
        var filter      = RecordFilter.ByStreamPrefix("order");
        var matching    = _faker.With("order-events");
        var nonMatching = _faker.With("payment-events");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }
}
