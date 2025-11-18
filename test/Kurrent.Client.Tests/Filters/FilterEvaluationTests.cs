using Kurrent.Client;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class FilterEvaluationTests {
    readonly IFilterExpressionParser _parser = new FilterExpressionParser();

    #region Helpers

    static Record CreateRecord(
        string stream = "test-stream",
        string schemaName = "TestEvent",
        DateTime? timestamp = null,
        long position = 0,
        long streamRevision = 0,
        Metadata? metadata = null) {
        var meta = metadata ?? new Metadata();

        meta = meta
            .With(SystemMetadataKeys.SchemaName, schemaName)
            .With(SystemMetadataKeys.SchemaFormat, "json");

        return new Record {
            Id = Guid.NewGuid(),
            Stream = stream,
            Position = position,
            StreamRevision = streamRevision,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Metadata = meta
        };
    }

    IFilterExpression Parse(string expression) {
        var result = _parser.Parse(expression);
        result.IsSuccess.ShouldBeTrue($"Failed to parse: {expression}");
        return result.Value;
    }

    #endregion

    #region Comparison Operators

    [Test]
    public void evaluates_stream_equality() {
        var filter = Parse("stream = 'orders'");
        var matchingRecord = CreateRecord(stream: "orders");
        var nonMatchingRecord = CreateRecord(stream: "payments");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_stream_equality_case_insensitive() {
        var filter = Parse("stream = 'ORDERS'");
        var record = CreateRecord(stream: "orders");

        filter.IsMatch(record).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_equals() {
        var filter = Parse("stream != 'test'");
        var matchingRecord = CreateRecord(stream: "orders");
        var nonMatchingRecord = CreateRecord(stream: "test");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_greater_than() {
        var filter = Parse("position > 100");
        var matchingRecord = CreateRecord(position: 150);
        var nonMatchingRecord = CreateRecord(position: 50);

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_less_than() {
        var filter = Parse("position < 100");
        var matchingRecord = CreateRecord(position: 50);
        var nonMatchingRecord = CreateRecord(position: 150);

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_greater_than_or_equal() {
        var filter = Parse("position >= 100");
        var matchingEqual = CreateRecord(position: 100);
        var matchingGreater = CreateRecord(position: 150);
        var nonMatching = CreateRecord(position: 50);

        filter.IsMatch(matchingEqual).ShouldBeTrue();
        filter.IsMatch(matchingGreater).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_less_than_or_equal() {
        var filter = Parse("position <= 100");
        var matchingEqual = CreateRecord(position: 100);
        var matchingLess = CreateRecord(position: 50);
        var nonMatching = CreateRecord(position: 150);

        filter.IsMatch(matchingEqual).ShouldBeTrue();
        filter.IsMatch(matchingLess).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_timestamp_comparison() {
        var filter = Parse("timestamp >= '2024-01-01T00:00:00Z'");
        var matchingRecord = CreateRecord(timestamp: new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var nonMatchingRecord = CreateRecord(timestamp: new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc));

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    #endregion

    #region Logical Operators

    [Test]
    public void evaluates_and_expression() {
        var filter = Parse("stream = 'orders' AND schema_name = 'OrderCreated'");
        var matchingRecord = CreateRecord(stream: "orders", schemaName: "OrderCreated");
        var wrongStream = CreateRecord(stream: "payments", schemaName: "OrderCreated");
        var wrongSchema = CreateRecord(stream: "orders", schemaName: "OrderUpdated");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(wrongStream).ShouldBeFalse();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
    }

    [Test]
    public void evaluates_or_expression() {
        var filter = Parse("stream = 'orders' OR stream = 'payments'");
        var ordersRecord = CreateRecord(stream: "orders");
        var paymentsRecord = CreateRecord(stream: "payments");
        var usersRecord = CreateRecord(stream: "users");

        filter.IsMatch(ordersRecord).ShouldBeTrue();
        filter.IsMatch(paymentsRecord).ShouldBeTrue();
        filter.IsMatch(usersRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_expression() {
        var filter = Parse("NOT (stream = 'test')");
        var matchingRecord = CreateRecord(stream: "orders");
        var nonMatchingRecord = CreateRecord(stream: "test");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_complex_logical_expression() {
        var filter = Parse("(stream = 'orders' OR stream = 'payments') AND schema_name = 'Created'");
        var matchingOrders = CreateRecord(stream: "orders", schemaName: "Created");
        var matchingPayments = CreateRecord(stream: "payments", schemaName: "Created");
        var wrongSchema = CreateRecord(stream: "orders", schemaName: "Updated");
        var wrongStream = CreateRecord(stream: "users", schemaName: "Created");

        filter.IsMatch(matchingOrders).ShouldBeTrue();
        filter.IsMatch(matchingPayments).ShouldBeTrue();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
        filter.IsMatch(wrongStream).ShouldBeFalse();
    }

    [Test]
    public void evaluates_and_with_short_circuit() {
        var filter = Parse("stream = 'never' AND properties.missing > 100");
        var record = CreateRecord(stream: "orders");

        filter.IsMatch(record).ShouldBeFalse();
    }

    [Test]
    public void evaluates_or_with_short_circuit() {
        var filter = Parse("stream = 'orders' OR properties.missing > 100");
        var record = CreateRecord(stream: "orders");

        filter.IsMatch(record).ShouldBeTrue();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public void evaluates_like_with_percent_wildcard() {
        var filter = Parse("stream LIKE 'order%'");
        var matching1 = CreateRecord(stream: "orders");
        var matching2 = CreateRecord(stream: "order-123");
        var nonMatching = CreateRecord(stream: "payments");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_like_with_underscore_wildcard() {
        var filter = Parse("stream LIKE 'order_'");
        var matching = CreateRecord(stream: "orders");
        var nonMatching1 = CreateRecord(stream: "order");
        var nonMatching2 = CreateRecord(stream: "order-123");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching1).ShouldBeFalse();
        filter.IsMatch(nonMatching2).ShouldBeFalse();
    }

    [Test]
    public void evaluates_like_case_sensitive() {
        var filter = Parse("stream LIKE 'Order%'");
        var matching = CreateRecord(stream: "Orders");
        var nonMatching = CreateRecord(stream: "orders");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_ilike_case_insensitive() {
        var filter = Parse("stream ILIKE 'order%'");
        var matching1 = CreateRecord(stream: "ORDERS");
        var matching2 = CreateRecord(stream: "Orders");
        var matching3 = CreateRecord(stream: "orders");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(matching3).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_like() {
        var filter = Parse("stream NOT LIKE '%test%'");
        var matching = CreateRecord(stream: "orders");
        var nonMatching = CreateRecord(stream: "test-stream");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_ilike() {
        var filter = Parse("stream NOT ILIKE '%TEST%'");
        var matching = CreateRecord(stream: "orders");
        var nonMatching = CreateRecord(stream: "my-test-stream");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Null Checking

    [Test]
    public void evaluates_is_null_for_missing_property() {
        var filter = Parse("properties.correlationId IS NULL");
        var recordWithoutProp = CreateRecord();
        var recordWithProp = CreateRecord(metadata: new Metadata().With("correlationId", "abc"));

        filter.IsMatch(recordWithoutProp).ShouldBeTrue();
        filter.IsMatch(recordWithProp).ShouldBeFalse();
    }

    [Test]
    public void evaluates_is_not_null() {
        var filter = Parse("properties.userId IS NOT NULL");
        var recordWithProp = CreateRecord(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = CreateRecord();

        filter.IsMatch(recordWithProp).ShouldBeTrue();
        filter.IsMatch(recordWithoutProp).ShouldBeFalse();
    }

    #endregion

    #region Set Membership

    [Test]
    public void evaluates_in_expression() {
        var filter = Parse("stream IN ('orders', 'payments', 'refunds')");
        var matching1 = CreateRecord(stream: "orders");
        var matching2 = CreateRecord(stream: "payments");
        var matching3 = CreateRecord(stream: "refunds");
        var nonMatching = CreateRecord(stream: "users");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(matching3).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_in_case_insensitive() {
        var filter = Parse("stream IN ('ORDERS', 'PAYMENTS')");
        var matching = CreateRecord(stream: "orders");

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_in_expression() {
        var filter = Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");
        var matching = CreateRecord(schemaName: "OrderCreated");
        var nonMatching = CreateRecord(schemaName: "SystemEvent");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_in_with_numbers() {
        var filter = Parse("position IN (100, 200, 300)");
        var matching = CreateRecord(position: 200);
        var nonMatching = CreateRecord(position: 150);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Range Operators

    [Test]
    public void evaluates_between_inclusive() {
        var filter = Parse("position BETWEEN 100 AND 200");
        var matchingLower = CreateRecord(position: 100);
        var matchingMiddle = CreateRecord(position: 150);
        var matchingUpper = CreateRecord(position: 200);
        var nonMatchingBelow = CreateRecord(position: 50);
        var nonMatchingAbove = CreateRecord(position: 250);

        filter.IsMatch(matchingLower).ShouldBeTrue();
        filter.IsMatch(matchingMiddle).ShouldBeTrue();
        filter.IsMatch(matchingUpper).ShouldBeTrue();
        filter.IsMatch(nonMatchingBelow).ShouldBeFalse();
        filter.IsMatch(nonMatchingAbove).ShouldBeFalse();
    }

    [Test]
    public void evaluates_between_with_dates() {
        var filter = Parse("timestamp BETWEEN '2024-01-01' AND '2024-12-31'");
        var matching = CreateRecord(timestamp: new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var nonMatchingBefore = CreateRecord(timestamp: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        var nonMatchingAfter = CreateRecord(timestamp: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatchingBefore).ShouldBeFalse();
        filter.IsMatch(nonMatchingAfter).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_between() {
        var filter = Parse("position NOT BETWEEN 100 AND 200");
        var matching1 = CreateRecord(position: 50);
        var matching2 = CreateRecord(position: 250);
        var nonMatching = CreateRecord(position: 150);

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Property Access

    [Test]
    public void evaluates_properties_bracket_notation() {
        var filter = Parse("properties['userId'] = 'user-123'");
        var matching = CreateRecord(metadata: new Metadata().With("userId", "user-123"));
        var nonMatching = CreateRecord(metadata: new Metadata().With("userId", "user-456"));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_properties_dot_notation() {
        var filter = Parse("properties.amount > 100");
        var matching = CreateRecord(metadata: new Metadata().With("amount", 150));
        var nonMatching = CreateRecord(metadata: new Metadata().With("amount", 50));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_props_alias() {
        var filter = Parse("props.correlationId IS NOT NULL");
        var matching = CreateRecord(metadata: new Metadata().With("correlationId", "abc-123"));
        var nonMatching = CreateRecord();

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_missing_property_as_null() {
        var filter = Parse("properties.missing = 'value'");
        var record = CreateRecord();

        filter.IsMatch(record).ShouldBeFalse();
    }

    #endregion

    #region Type Coercion

    [Test]
    public void coerces_string_to_number_for_comparison() {
        var filter = Parse("properties.amount > 100");
        var matching = CreateRecord(metadata: new Metadata().With("amount", "150"));

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void coerces_number_to_string_for_comparison() {
        var filter = Parse("properties.code = '42'");
        var matching = CreateRecord(metadata: new Metadata().With("code", 42));

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void handles_boolean_property_values() {
        var filter = Parse("properties.active = true");
        var matching = CreateRecord(metadata: new Metadata().With("active", true));
        var nonMatching = CreateRecord(metadata: new Metadata().With("active", false));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public void evaluates_complex_business_logic() {
        var filter = Parse(@"
            stream = 'orders' AND
            properties.amount > 100 AND
            properties.region IN ('us-east', 'us-west') AND
            properties.customerType != 'internal'
        ");

        var matching = CreateRecord(
            stream: "orders",
            metadata: new Metadata()
                .With("amount", 150)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongAmount = CreateRecord(
            stream: "orders",
            metadata: new Metadata()
                .With("amount", 50)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongRegion = CreateRecord(
            stream: "orders",
            metadata: new Metadata()
                .With("amount", 150)
                .With("region", "eu-west")
                .With("customerType", "external")
        );

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(wrongAmount).ShouldBeFalse();
        filter.IsMatch(wrongRegion).ShouldBeFalse();
    }

    [Test]
    public void evaluates_nested_or_with_and() {
        var filter = Parse("(schema_name = 'OrderCreated' OR schema_name = 'OrderUpdated') AND stream = 'orders'");

        var matchingCreated = CreateRecord(stream: "orders", schemaName: "OrderCreated");
        var matchingUpdated = CreateRecord(stream: "orders", schemaName: "OrderUpdated");
        var wrongStream = CreateRecord(stream: "payments", schemaName: "OrderCreated");
        var wrongSchema = CreateRecord(stream: "orders", schemaName: "OrderDeleted");

        filter.IsMatch(matchingCreated).ShouldBeTrue();
        filter.IsMatch(matchingUpdated).ShouldBeTrue();
        filter.IsMatch(wrongStream).ShouldBeFalse();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void handles_empty_string_comparison() {
        var filter = Parse("properties.tag = ''");
        var matching = CreateRecord(metadata: new Metadata().With("tag", ""));
        var nonMatching = CreateRecord(metadata: new Metadata().With("tag", "value"));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void handles_zero_in_numeric_comparison() {
        var filter = Parse("position = 0");
        var matching = CreateRecord(position: 0);
        var nonMatching = CreateRecord(position: 1);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void handles_negative_numbers() {
        var filter = Parse("properties.offset < 0");
        var matching = CreateRecord(metadata: new Metadata().With("offset", -10));
        var nonMatching = CreateRecord(metadata: new Metadata().With("offset", 10));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion
}
