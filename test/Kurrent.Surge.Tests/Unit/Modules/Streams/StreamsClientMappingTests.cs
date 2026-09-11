using Kurrent.Surge.Client.Streams;

using Contracts = KurrentDB.Protocol.V1.Streams;
using FilterCase = KurrentDB.Protocol.V1.Streams.ReadReq.Types.Options.Types.FilterOptions.FilterOneofCase;
using FilterOptionCase = KurrentDB.Protocol.V1.Streams.ReadReq.Types.Options.FilterOptionOneofCase;
using StreamOptionCase = KurrentDB.Protocol.V1.Streams.ReadReq.Types.Options.StreamOptionOneofCase;

namespace Kurrent.Surge.Tests.Modules.Streams;

[Category("Streams")]
public class StreamsClientMappingTests {
    const uint Window = 64;

    static Contracts.ReadReq Subscription(RecordFilter filter) =>
        StreamsClientMapping.Requests.CreateSubscriptionRequest(filter, RecordPosition.Earliest, Window);

    [Test]
    public async ValueTask schema_alternation_maps_to_an_event_type_regex() {
        // Arrange
        var filter        = RecordFilter.ByExpression("schema_name IN ('OrderPlaced', 'OrderShipped')");
        var expectedRegex = "^(OrderPlaced|OrderShipped)$";

        // Act
        var request = Subscription(filter);

        // Assert
        await Assert.That(request.Options.StreamOptionCase).IsEqualTo(StreamOptionCase.All);
        await Assert.That(request.Options.Filter.FilterCase).IsEqualTo(FilterCase.EventType);
        await Assert.That(request.Options.Filter.EventType.Regex).IsEqualTo(expectedRegex);
        await Assert.That(request.Options.Filter.Max).IsEqualTo(Window);
        await Assert.That(request.Options.Filter.CheckpointIntervalMultiplier).IsEqualTo(1u);
    }

    [Test]
    public async ValueTask unroutable_expression_maps_to_the_system_stream_exclusion_regex() {
        // Arrange
        var filter        = RecordFilter.ByExpression("properties['amount'] > 100");
        var expectedRegex = @"^[^\$]";

        // Act
        var request = Subscription(filter);

        // Assert — the request never carries NoFilter, so the server drops the system streams.
        await Assert.That(request.Options.StreamOptionCase).IsEqualTo(StreamOptionCase.All);
        await Assert.That(request.Options.Filter.FilterCase).IsEqualTo(FilterCase.StreamIdentifier);
        await Assert.That(request.Options.Filter.StreamIdentifier.Regex).IsEqualTo(expectedRegex);
        await Assert.That(request.Options.ResolveLinks).IsFalse();
    }

    [Test]
    public async ValueTask exclude_system_records_maps_to_a_stream_identifier_regex() {
        // Arrange
        var filter        = RecordFilter.ExcludeSystemRecords;
        var expectedRegex = @"^[^\$]";

        // Act
        var request = Subscription(filter);

        // Assert — the default filter is a server-checkpointed $all scan with the scan window applied.
        await Assert.That(request.Options.StreamOptionCase).IsEqualTo(StreamOptionCase.All);
        await Assert.That(request.Options.Filter.FilterCase).IsEqualTo(FilterCase.StreamIdentifier);
        await Assert.That(request.Options.Filter.StreamIdentifier.Regex).IsEqualTo(expectedRegex);
        await Assert.That(request.Options.Filter.Max).IsEqualTo(Window);
        await Assert.That(request.Options.Filter.CheckpointIntervalMultiplier).IsEqualTo(1u);
        await Assert.That(request.Options.ResolveLinks).IsFalse();
    }

    [Test]
    public async ValueTask stream_pattern_maps_to_a_stream_identifier_regex() {
        // Arrange
        var filter        = RecordFilter.ByExpression("stream LIKE 'orders-%'");
        var expectedRegex = "^orders-.*$";

        // Act
        var request = Subscription(filter);

        // Assert
        await Assert.That(request.Options.Filter.FilterCase).IsEqualTo(FilterCase.StreamIdentifier);
        await Assert.That(request.Options.Filter.StreamIdentifier.Regex).IsEqualTo(expectedRegex);
    }

    [Test]
    public async ValueTask schema_equality_maps_to_a_stream_identifier_prefix() {
        // Arrange
        var filter         = RecordFilter.BySchema("OrderPlaced");
        var expectedPrefix = "$idx-et-OrderPlaced";

        // Act
        var request = Subscription(filter);

        // Assert
        await Assert.That(request.Options.Filter.FilterCase).IsEqualTo(FilterCase.StreamIdentifier);
        await Assert.That(request.Options.Filter.StreamIdentifier.Prefix).IsEquivalentTo([expectedPrefix]);
    }

    [Test]
    public async ValueTask empty_filter_maps_to_no_filter() {
        // Arrange
        var filter = RecordFilter.None;

        // Act
        var request = Subscription(filter);

        // Assert — the raw log stays raw: links included, never resolved.
        await Assert.That(request.Options.StreamOptionCase).IsEqualTo(StreamOptionCase.All);
        await Assert.That(request.Options.FilterOptionCase).IsEqualTo(FilterOptionCase.NoFilter);
        await Assert.That(request.Options.ResolveLinks).IsFalse();
    }

    [Test]
    public async ValueTask stream_equality_maps_to_a_direct_stream_read() {
        // Arrange
        var filter = RecordFilter.ByExpression("stream = 'orders-42'");

        // Act
        var request = Subscription(filter);

        // Assert — a stream route is the only one that resolves links.
        await Assert.That(request.Options.StreamOptionCase).IsEqualTo(StreamOptionCase.Stream);
        await Assert.That(request.Options.FilterOptionCase).IsEqualTo(FilterOptionCase.NoFilter);
        await Assert.That(request.Options.ResolveLinks).IsTrue();
    }
}
