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
    public async Task by_stream_matches_exact_stream() {
        var filter      = RecordFilter.ByStream(OrdersStream);
        var matching    = _faker.With(OrdersStream);
        var nonMatching = _faker.With(PaymentsStream);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region ByStreamPattern - Auto-Derivation of TargetStream

    [Test]
    public async Task by_stream_pattern_exact_match_derives_target_stream() {
        // Note: '-' doesn't need escaping in regex (only inside [] character classes)
        var filter = RecordFilter.ByStreamPattern($"^{Regex.Escape(OrdersStream)}$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From(OrdersStream));
    }

    [Test]
    public async Task by_stream_pattern_with_escaped_dot_derives_target_stream() {
        // Only '.' needs escaping, '-' is literal in regex
        var filter = RecordFilter.ByStreamPattern(@"^Orders\.v1-ABC123$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("Orders.v1-ABC123"));
        await Assert.That(filter.Expression).IsEqualTo(@"^Orders\.v1-ABC123$");
    }

    [Test]
    public async Task by_stream_pattern_category_wildcard_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^Orders-.*$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
    }

    [Test]
    public async Task by_stream_pattern_with_single_char_wildcard_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^Orders-.$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
    }

    [Test]
    public async Task by_stream_pattern_case_insensitive_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("(?i)^ORDERS-ABC123$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
    }

    [Test]
    public async Task by_stream_pattern_with_alternation_has_no_target_stream() {
        var filter = RecordFilter.ByStreamPattern("^(Orders-001|Payments-002)$");

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
    }

    [Test]
    public async Task by_stream_pattern_matches_category() {
        var filter      = RecordFilter.ByStreamPattern("^Orders-.*$");
        var matching    = _faker.With(OrdersStream);
        var nonMatching = _faker.With(PaymentsStream);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task by_stream_pattern_case_insensitive_matches_records() {
        var filter = RecordFilter.ByStreamPattern("(?i)^ORDERS-ABC123$");

        await Assert.That(filter.IsMatch(_faker.With("ORDERS-ABC123"))).IsTrue();
        await Assert.That(filter.IsMatch(_faker.With("orders-ABC123"))).IsTrue();
        await Assert.That(filter.IsMatch(_faker.With("Orders-ABC123"))).IsTrue();
        await Assert.That(filter.IsMatch(_faker.With("Payments-ABC123"))).IsFalse();
    }

    #endregion
}
