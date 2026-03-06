using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class SchemaFilterTests {
    [Test]
    public async Task by_schema_targets_idx_et_stream() {
        var filter = RecordFilter.BySchema("OrderCreated");

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-et-OrderCreated"));
    }

    [Test]
    public async Task by_schema_produces_exact_regex() {
        var filter = RecordFilter.BySchema("OrderCreated");

        await Assert.That(filter.Expression).IsEqualTo(@"^\$idx-et-OrderCreated$");
    }

    [Test]
    public async Task by_schema_escapes_special_characters() {
        var filter = RecordFilter.BySchema("Order.Created.V1");

        await Assert.That(filter.Expression).IsEqualTo(@"^\$idx-et-Order\.Created\.V1$");
    }
}
