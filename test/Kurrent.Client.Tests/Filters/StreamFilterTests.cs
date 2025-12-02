using System.Text.RegularExpressions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class StreamFilterTests {
    readonly RecordFaker _faker = new();

    // Realistic stream names following KurrentDB convention: {Category}-{Id}
    const string OrdersStream   = "Orders-D0332EB7480643E481BF3205296958FD";
    const string PaymentsStream = "Payments-A1B2C3D4E5F6789012345678ABCDEF01";
    const string UsersStream    = "Users-F9E8D7C6B5A4321098765432FEDCBA98";

    #region ByStream

    [Test]
    public void by_stream_matches_exact_stream() {
        var filter      = RecordFilter.ByStream(OrdersStream);
        var matching    = _faker.With(OrdersStream);
        var nonMatching = _faker.With(PaymentsStream);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region ByStreamPrefix

    [Test]
    public void by_stream_prefix_matches_category_prefixes() {
        var filter           = RecordFilter.ByStreamPrefix("Orders-", "Payments-");
        var matchingOrders   = _faker.With(OrdersStream);
        var matchingPayments = _faker.With(PaymentsStream);
        var nonMatching      = _faker.With(UsersStream);

        filter.IsMatch(matchingOrders).ShouldBeTrue();
        filter.IsMatch(matchingPayments).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void by_stream_prefix_with_single_category() {
        var filter      = RecordFilter.ByStreamPrefix("Orders-");
        var matching    = _faker.With(OrdersStream);
        var nonMatching = _faker.With(PaymentsStream);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region ByStreamPattern - Auto-Derivation of TargetStream

    [Test]
    public void by_stream_pattern_exact_match_derives_target_stream() {
        // Note: '-' doesn't need escaping in regex (only inside [] character classes)
        var filter = RecordFilter.ByStreamPattern($"^{Regex.Escape(OrdersStream)}$");

        filter.TargetStream.ShouldBe(StreamName.From(OrdersStream));
    }

    [Test]
    public void by_stream_pattern_with_escaped_dot_derives_target_stream() {
        // Only '.' needs escaping, '-' is literal in regex
        var filter = RecordFilter.ByStreamPattern(@"^Orders\.v1-ABC123$");

        filter.TargetStream.ShouldBe(StreamName.From("Orders.v1-ABC123"));
        filter.Expression.ShouldBe(@"^Orders\.v1-ABC123$");
    }

    [Test]
    public void by_stream_pattern_category_wildcard_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^Orders-.*$");

        filter.TargetStream.ShouldBe(StreamName.All);
    }

    [Test]
    public void by_stream_pattern_with_single_char_wildcard_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^Orders-.$");

        filter.TargetStream.ShouldBe(StreamName.All);
    }

    [Test]
    public void by_stream_pattern_case_insensitive_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("(?i)^ORDERS-ABC123$");

        filter.TargetStream.ShouldBe(StreamName.All);
    }

    [Test]
    public void by_stream_pattern_with_alternation_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^(Orders-001|Payments-002)$");

        filter.TargetStream.ShouldBe(StreamName.All);
    }

    [Test]
    public void by_stream_pattern_matches_category() {
        var filter      = RecordFilter.ByStreamPattern("^Orders-.*$");
        var matching    = _faker.With(OrdersStream);
        var nonMatching = _faker.With(PaymentsStream);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void by_stream_pattern_case_insensitive_matches_records() {
        var filter = RecordFilter.ByStreamPattern("(?i)^ORDERS-ABC123$");

        filter.IsMatch(_faker.With("ORDERS-ABC123")).ShouldBeTrue();
        filter.IsMatch(_faker.With("orders-ABC123")).ShouldBeTrue();
        filter.IsMatch(_faker.With("Orders-ABC123")).ShouldBeTrue();
        filter.IsMatch(_faker.With("Payments-ABC123")).ShouldBeFalse();
    }

    #endregion
}
