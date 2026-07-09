using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Processors.Partitioning;
using Kurrent.Surge.Streaming.Tests.Fakes;

namespace Kurrent.Surge.Streaming.Tests.Partitioning;

[Category("Processors")]
public class PartitionKeyExtractorTests {
    readonly RecordFaker _faker = new();

    #region ->> Factory Methods <<-

    [Test]
    public async ValueTask create_returns_none_for_empty_expression() {
        var extractor = PartitionKeyExtractor.Create("");

        await Assert.That(extractor).IsEqualTo(PartitionKeyExtractor.None);
    }

    [Test]
    public async ValueTask create_returns_none_for_whitespace_expression() {
        var extractor = PartitionKeyExtractor.Create("   ");

        await Assert.That(extractor).IsEqualTo(PartitionKeyExtractor.None);
    }

    [Test]
    public async ValueTask create_throws_for_invalid_expression() {
        await Assert.That(() => PartitionKeyExtractor.Create("!!!invalid")).Throws<ArgumentException>();
    }

    #endregion // Factory Methods

    #region ->> Built-in Property Extraction <<-

    [Test]
    public async ValueTask extracts_stream_value() {
        var extractor = PartitionKeyExtractor.Create("stream");
        var record    = _faker.With(stream: "orders-123");

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("orders-123");
    }

    [Test]
    public async ValueTask extracts_schema_name_value() {
        var extractor = PartitionKeyExtractor.Create("schema_name");
        var record    = _faker.With(schemaName: "OrderCreated");

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("OrderCreated");
    }

    [Test]
    public async ValueTask extracts_schema_alias_value() {
        var extractor = PartitionKeyExtractor.Create("schema");
        var record    = _faker.With(schemaName: "OrderCreated");

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("OrderCreated");
    }

    [Test]
    public async ValueTask extracts_position_value() {
        var extractor = PartitionKeyExtractor.Create("position");
        var record    = _faker.With(position: 12345);

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("12345");
    }

    [Test]
    public async ValueTask extracts_pos_alias_value() {
        var extractor = PartitionKeyExtractor.Create("pos");
        var record    = _faker.With(position: 12345);

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("12345");
    }

    [Test]
    public async ValueTask extracts_timestamp_value() {
        var extractor = PartitionKeyExtractor.Create("timestamp");
        var timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var record    = _faker.With(timestamp: timestamp);

        var key = extractor.Extract(record);

        await Assert.That(key).IsNotNull();
        await Assert.That(key).IsEqualTo("1718447400000");
    }

    [Test]
    public async ValueTask extracts_ts_alias_value() {
        var extractor = PartitionKeyExtractor.Create("ts");
        var timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var record    = _faker.With(timestamp: timestamp);

        var key = extractor.Extract(record);

        await Assert.That(key).IsNotNull();
        await Assert.That(key).IsEqualTo("1718447400000");
    }

    [Test]
    public async ValueTask extracts_stream_revision_value() {
        var extractor = PartitionKeyExtractor.Create("stream_revision");
        var record    = _faker.With(streamRevision: 42);

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("42");
    }

    [Test]
    public async ValueTask extracts_revision_alias_value() {
        var extractor = PartitionKeyExtractor.Create("revision");
        var record    = _faker.With(streamRevision: 42);

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("42");
    }

    [Test]
    public async ValueTask extracts_record_id_value() {
        var extractor = PartitionKeyExtractor.Create("record_id");
        var record    = _faker.With();

        var key = extractor.Extract(record);

        await Assert.That(key).IsNotNull();
        // Guid without dashes (for consistent hashing)
        await Assert.That(key!).DoesNotContain("-");
        await Assert.That(key.Length).IsEqualTo(32);
    }

    [Test]
    public async ValueTask extracts_id_alias_value() {
        var extractor = PartitionKeyExtractor.Create("record_id");
        var record    = _faker.With();

        var key = extractor.Extract(record);

        await Assert.That(key).IsNotNull();
        await Assert.That(key!.Length).IsEqualTo(32);
    }

    #endregion // Built-in Property Extraction

    #region ->> Metadata Property Extraction <<-

    [Test]
    public async ValueTask extracts_properties_bracket_notation() {
        var extractor = PartitionKeyExtractor.Create("properties['tenantId']");
        var record    = _faker.With(metadata: new Metadata().With("tenantId", "tenant-abc"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("tenant-abc");
    }

    [Test]
    public async ValueTask extracts_properties_dot_notation() {
        var extractor = PartitionKeyExtractor.Create("properties.customerId");
        var record    = _faker.With(metadata: new Metadata().With("customerId", "cust-123"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("cust-123");
    }

    [Test]
    public async ValueTask extracts_props_alias_bracket_notation() {
        var extractor = PartitionKeyExtractor.Create("props['region']");
        var record    = _faker.With(metadata: new Metadata().With("region", "us-east"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("us-east");
    }

    [Test]
    public async ValueTask extracts_props_alias_dot_notation() {
        var extractor = PartitionKeyExtractor.Create("props.region");
        var record    = _faker.With(metadata: new Metadata().With("region", "us-east"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("us-east");
    }

    [Test]
    public async ValueTask returns_null_for_missing_property() {
        var extractor = PartitionKeyExtractor.Create("properties['missing']");
        var record    = _faker.With();

        var key = extractor.Extract(record);

        await Assert.That(key).IsNull();
    }

    [Test]
    public async ValueTask extracts_numeric_property_as_string() {
        var extractor = PartitionKeyExtractor.Create("properties.amount");
        var record    = _faker.With(metadata: new Metadata().With("amount", 12345));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("12345");
    }

    [Test]
    public async ValueTask extracts_guid_property_without_dashes() {
        var extractor = PartitionKeyExtractor.Create("properties.correlationId");
        var guid      = Guid.NewGuid();
        var record    = _faker.With(metadata: new Metadata().With("correlationId", guid));

        var key = extractor.Extract(record);

        await Assert.That(key).IsNotNull();
        await Assert.That(key!).IsEqualTo(guid.ToString("N"));
        await Assert.That(key).DoesNotContain("-");
    }

    #endregion // Metadata Property Extraction

    #region ->> Expression Property <<-

    [Test]
    public async ValueTask preserves_original_expression() {
        var extractor = PartitionKeyExtractor.Create("properties['userId']");

        await Assert.That(extractor.Expression).IsEqualTo("properties['userId']");
    }

    [Test]
    public async ValueTask none_extractor_has_empty_expression() {
        await Assert.That(PartitionKeyExtractor.None.Expression).IsEqualTo("");
    }

    #endregion // Expression Property

    #region ->> Parser Edge Cases <<-

    [Test]
    public async ValueTask handles_whitespace_around_expression() {
        var extractor = PartitionKeyExtractor.Create("  stream  ");
        var record    = _faker.With(stream: "orders");

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("orders");
    }

    [Test]
    public async ValueTask handles_bracket_key_with_special_characters() {
        var extractor = PartitionKeyExtractor.Create("properties['user.id']");
        var record    = _faker.With(metadata: new Metadata().With("user.id", "u123"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("u123");
    }

    [Test]
    public async ValueTask handles_bracket_key_with_spaces() {
        var extractor = PartitionKeyExtractor.Create("properties['my key']");
        var record    = _faker.With(metadata: new Metadata().With("my key", "value"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("value");
    }

    [Test]
    public async ValueTask handles_escaped_quote_in_bracket_key() {
        var extractor = PartitionKeyExtractor.Create("properties['it\\'s']");
        var record    = _faker.With(metadata: new Metadata().With("it's", "value"));

        var key = extractor.Extract(record);

        await Assert.That(key).IsEqualTo("value");
    }

    [Test]
    public async ValueTask rejects_trailing_content() {
        await Assert.That(() => PartitionKeyExtractor.Create("stream extra")).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask rejects_comparison_operators() {
        await Assert.That(() => PartitionKeyExtractor.Create("stream = 'orders'")).Throws<ArgumentException>();
    }

    [Test]
    public async ValueTask rejects_logical_operators() {
        await Assert.That(() => PartitionKeyExtractor.Create("stream AND schema")).Throws<ArgumentException>();
    }

    #endregion // Parser Edge Cases

    #region ->> Case Insensitivity <<-

    [Test]
    public async ValueTask property_names_are_case_insensitive() {
        var extractors = new[] {
            PartitionKeyExtractor.Create("STREAM"),
            PartitionKeyExtractor.Create("Stream"),
            PartitionKeyExtractor.Create("stream")
        };

        var record = _faker.With(stream: "test-stream");

        foreach (var extractor in extractors)
            await Assert.That(extractor.Extract(record)).IsEqualTo("test-stream");
    }

    [Test]
    public async ValueTask property_aliases_are_case_insensitive() {
        var extractors = new[] {
            PartitionKeyExtractor.Create("PROPS.key"),
            PartitionKeyExtractor.Create("Props.key"),
            PartitionKeyExtractor.Create("props.key")
        };

        var record = _faker.With(metadata: new Metadata().With("key", "value"));

        foreach (var extractor in extractors)
            await Assert.That(extractor.Extract(record)).IsEqualTo("value");
    }

    #endregion // Case Insensitivity

    #region ->> Determinism <<-

    [Test]
    public async ValueTask extraction_is_deterministic() {
        var extractor = PartitionKeyExtractor.Create("properties.key");
        var record    = _faker.With(metadata: new Metadata().With("key", "consistent-value"));

        var key1 = extractor.Extract(record);
        var key2 = extractor.Extract(record);
        var key3 = extractor.Extract(record);

        await Assert.That(key1).IsEqualTo(key2);
        await Assert.That(key2).IsEqualTo(key3);
    }

    [Test]
    public async ValueTask same_expression_produces_same_results() {
        var extractor1 = PartitionKeyExtractor.Create("stream");
        var extractor2 = PartitionKeyExtractor.Create("stream");
        var record     = _faker.With(stream: "test");

        await Assert.That(extractor1.Extract(record)).IsEqualTo(extractor2.Extract(record));
    }

    #endregion // Determinism

    #region ->> Integration with PartitionCalculator <<-

    [Test]
    public async ValueTask extracted_key_works_with_partition_calculator() {
        var extractor  = PartitionKeyExtractor.Create("properties['tenantId']");
        var calculator = PartitionCalculator.XXHash;
        var record     = _faker.With(metadata: new Metadata().With("tenantId", "tenant-123"));

        var key = extractor.Extract(record);
        await Assert.That(key).IsNotNull();

        var partition = calculator.Calculate(key!, maxParallelism: 128);

        await Assert.That(partition).IsGreaterThanOrEqualTo(0);
        await Assert.That(partition).IsLessThan(128);
    }

    [Test]
    public async ValueTask same_key_always_maps_to_same_partition() {
        var extractor  = PartitionKeyExtractor.Create("properties.customerId");
        var calculator = PartitionCalculator.XXHash;

        var record1 = _faker.With(metadata: new Metadata().With("customerId", "cust-abc"));
        var record2 = _faker.With(metadata: new Metadata().With("customerId", "cust-abc"));

        var key1 = extractor.Extract(record1);
        var key2 = extractor.Extract(record2);

        await Assert.That(key1).IsEqualTo(key2);

        var partition1 = calculator.Calculate(key1!, 128);
        var partition2 = calculator.Calculate(key2!, 128);

        await Assert.That(partition1).IsEqualTo(partition2);
    }

    #endregion // Integration with PartitionCalculator
}
