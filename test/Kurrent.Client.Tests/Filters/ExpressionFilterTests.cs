using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterTests {
    readonly ExpressionFilterParser _parser = new();
    readonly RecordFaker            _faker  = new();

    IFilterExpression Parse(string expression) {
        var result = _parser.Parse(expression);
        result.IsSuccess.ShouldBeTrue($"Failed to parse: {expression}");
        return result.Value;
    }

    #region Comparison Operators

    [Test]
    public void evaluates_stream_equality() {
        var filter            = Parse("stream = 'orders'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("payments");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_stream_equality_case_insensitive() {
        var filter = Parse("stream = 'ORDERS'");
        var record = _faker.With("orders");

        filter.IsMatch(record).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_equals() {
        var filter            = Parse("stream != 'test'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_greater_than() {
        var filter            = Parse("position > 100");
        var matchingRecord    = _faker.With(position: 150);
        var nonMatchingRecord = _faker.With(position: 50);

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_less_than() {
        var filter            = Parse("position < 100");
        var matchingRecord    = _faker.With(position: 50);
        var nonMatchingRecord = _faker.With(position: 150);

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_greater_than_or_equal() {
        var filter          = Parse("position >= 100");
        var matchingEqual   = _faker.With(position: 100);
        var matchingGreater = _faker.With(position: 150);
        var nonMatching     = _faker.With(position: 50);

        filter.IsMatch(matchingEqual).ShouldBeTrue();
        filter.IsMatch(matchingGreater).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_less_than_or_equal() {
        var filter        = Parse("position <= 100");
        var matchingEqual = _faker.With(position: 100);
        var matchingLess  = _faker.With(position: 50);
        var nonMatching   = _faker.With(position: 150);

        filter.IsMatch(matchingEqual).ShouldBeTrue();
        filter.IsMatch(matchingLess).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_timestamp_comparison() {
        var filter = Parse("timestamp >= '2024-01-01T00:00:00Z'");
        var matchingRecord = _faker.With(
            timestamp: new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingRecord = _faker.With(
            timestamp: new DateTime(
                2023, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    #endregion

    #region Logical Operators

    [Test]
    public void evaluates_and_expression() {
        var filter         = Parse("stream = 'orders' AND schema_name = 'OrderCreated'");
        var matchingRecord = _faker.With("orders", "OrderCreated");
        var wrongStream    = _faker.With("payments", "OrderCreated");
        var wrongSchema    = _faker.With("orders", "OrderUpdated");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(wrongStream).ShouldBeFalse();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
    }

    [Test]
    public void evaluates_or_expression() {
        var filter         = Parse("stream = 'orders' OR stream = 'payments'");
        var ordersRecord   = _faker.With("orders");
        var paymentsRecord = _faker.With("payments");
        var usersRecord    = _faker.With("users");

        filter.IsMatch(ordersRecord).ShouldBeTrue();
        filter.IsMatch(paymentsRecord).ShouldBeTrue();
        filter.IsMatch(usersRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_expression() {
        var filter            = Parse("NOT (stream = 'test')");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        filter.IsMatch(matchingRecord).ShouldBeTrue();
        filter.IsMatch(nonMatchingRecord).ShouldBeFalse();
    }

    [Test]
    public void evaluates_complex_logical_expression() {
        var filter           = Parse("(stream = 'orders' OR stream = 'payments') AND schema_name = 'Created'");
        var matchingOrders   = _faker.With("orders", "Created");
        var matchingPayments = _faker.With("payments", "Created");
        var wrongSchema      = _faker.With("orders", "Updated");
        var wrongStream      = _faker.With("users", "Created");

        filter.IsMatch(matchingOrders).ShouldBeTrue();
        filter.IsMatch(matchingPayments).ShouldBeTrue();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
        filter.IsMatch(wrongStream).ShouldBeFalse();
    }

    [Test]
    public void evaluates_and_with_short_circuit() {
        var filter = Parse("stream = 'never' AND properties.missing > 100");
        var record = _faker.With("orders");

        filter.IsMatch(record).ShouldBeFalse();
    }

    [Test]
    public void evaluates_or_with_short_circuit() {
        var filter = Parse("stream = 'orders' OR properties.missing > 100");
        var record = _faker.With("orders");

        filter.IsMatch(record).ShouldBeTrue();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public void evaluates_like_with_percent_wildcard() {
        var filter      = Parse("stream LIKE 'order%'");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("order");
        var matching3   = _faker.With("orders-123");
        var nonMatching = _faker.With("payments");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(matching3).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_like_with_underscore_wildcard() {
        var filter       = Parse("stream LIKE 'order_'");
        var matching     = _faker.With("orders");
        var nonMatching1 = _faker.With("order");
        var nonMatching2 = _faker.With("order-123");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching1).ShouldBeFalse();
        filter.IsMatch(nonMatching2).ShouldBeFalse();
    }

    [Test]
    public void evaluates_like_with_exact_match() {
        var filter       = Parse("stream LIKE 'orders-99'");
        var matching     = _faker.With("orders-99");
        var nonMatching1 = _faker.With("orders-");
        var nonMatching2 = _faker.With("orders-123");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching1).ShouldBeFalse();
        filter.IsMatch(nonMatching2).ShouldBeFalse();
    }

    [Test]
    public void evaluates_like_case_sensitive() {
        var filter      = Parse("stream LIKE 'Order%'");
        var matching    = _faker.With("Orders");
        var nonMatching = _faker.With("orders");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_ilike_case_insensitive() {
        var filter    = Parse("stream ILIKE 'order%'");
        var matching1 = _faker.With("ORDERS-123");
        var matching2 = _faker.With("Orders-456");
        var matching3 = _faker.With("orders-789");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(matching3).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_like() {
        var filter      = Parse("stream NOT LIKE '%test%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("test-stream");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_ilike() {
        var filter      = Parse("stream NOT ILIKE '%TEST%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("my-test-stream");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Null Checking

    [Test]
    public void evaluates_is_null_for_missing_property() {
        var filter            = Parse("properties.correlationId IS NULL");
        var recordWithoutProp = _faker.With();
        var recordWithProp    = _faker.With(metadata: new Metadata().With("correlationId", "abc"));

        filter.IsMatch(recordWithoutProp).ShouldBeTrue();
        filter.IsMatch(recordWithProp).ShouldBeFalse();
    }

    [Test]
    public void evaluates_is_not_null() {
        var filter            = Parse("properties.userId IS NOT NULL");
        var recordWithProp    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = _faker.With();

        filter.IsMatch(recordWithProp).ShouldBeTrue();
        filter.IsMatch(recordWithoutProp).ShouldBeFalse();
    }

    #endregion

    #region Set Membership

    [Test]
    public void evaluates_in_expression() {
        var filter      = Parse("stream IN ('orders', 'payments', 'refunds')");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("payments");
        var matching3   = _faker.With("refunds");
        var nonMatching = _faker.With("users");

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(matching3).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_in_case_insensitive() {
        var filter   = Parse("stream IN ('ORDERS', 'PAYMENTS')");
        var matching = _faker.With("orders");

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void evaluates_not_in_expression() {
        var filter      = Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");
        var matching    = _faker.With(schemaName: "OrderCreated");
        var nonMatching = _faker.With(schemaName: "SystemEvent");

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_in_with_numbers() {
        var filter      = Parse("position IN (100, 200, 300)");
        var matching    = _faker.With(position: 200);
        var nonMatching = _faker.With(position: 150);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Range Operators

    [Test]
    public void evaluates_between_inclusive() {
        var filter           = Parse("position BETWEEN 100 AND 200");
        var matchingLower    = _faker.With(position: 100);
        var matchingMiddle   = _faker.With(position: 150);
        var matchingUpper    = _faker.With(position: 200);
        var nonMatchingBelow = _faker.With(position: 50);
        var nonMatchingAbove = _faker.With(position: 250);

        filter.IsMatch(matchingLower).ShouldBeTrue();
        filter.IsMatch(matchingMiddle).ShouldBeTrue();
        filter.IsMatch(matchingUpper).ShouldBeTrue();
        filter.IsMatch(nonMatchingBelow).ShouldBeFalse();
        filter.IsMatch(nonMatchingAbove).ShouldBeFalse();
    }

    [Test]
    public void evaluates_between_with_dates() {
        var filter = Parse("timestamp BETWEEN '2024-01-01' AND '2024-12-31'");
        var matching = _faker.With(
            timestamp: new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingBefore = _faker.With(
            timestamp: new DateTime(
                2023, 12, 31,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        var nonMatchingAfter = _faker.With(
            timestamp: new DateTime(
                2025, 1, 1,
                0, 0, 0,
                DateTimeKind.Utc
            )
        );

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatchingBefore).ShouldBeFalse();
        filter.IsMatch(nonMatchingAfter).ShouldBeFalse();
    }

    [Test]
    public void evaluates_not_between() {
        var filter      = Parse("position NOT BETWEEN 100 AND 200");
        var matching1   = _faker.With(position: 50);
        var matching2   = _faker.With(position: 250);
        var nonMatching = _faker.With(position: 150);

        filter.IsMatch(matching1).ShouldBeTrue();
        filter.IsMatch(matching2).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Property Access

    [Test]
    public void evaluates_properties_bracket_notation() {
        var filter      = Parse("properties['userId'] = 'user-123'");
        var matching    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var nonMatching = _faker.With(metadata: new Metadata().With("userId", "user-456"));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_properties_dot_notation() {
        var filter      = Parse("properties.amount > 100");
        var matching    = _faker.With(metadata: new Metadata().With("amount", 150));
        var nonMatching = _faker.With(metadata: new Metadata().With("amount", 50));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_props_alias() {
        var filter      = Parse("props.correlationId IS NOT NULL");
        var matching    = _faker.With(metadata: new Metadata().With("correlationId", "abc-123"));
        var nonMatching = _faker.With();

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void evaluates_missing_property_as_null() {
        var filter = Parse("properties.missing = 'value'");
        var record = _faker.With();

        filter.IsMatch(record).ShouldBeFalse();
    }

    #endregion

    #region Type Coercion

    [Test]
    public void coerces_string_to_number_for_comparison() {
        var filter   = Parse("properties.amount > 100");
        var matching = _faker.With(metadata: new Metadata().With("amount", "150"));

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void coerces_number_to_string_for_comparison() {
        var filter   = Parse("properties.code = '42'");
        var matching = _faker.With(metadata: new Metadata().With("code", 42));

        filter.IsMatch(matching).ShouldBeTrue();
    }

    [Test]
    public void handles_boolean_property_values() {
        var filter      = Parse("properties.active = true");
        var matching    = _faker.With(metadata: new Metadata().With("active", true));
        var nonMatching = _faker.With(metadata: new Metadata().With("active", false));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public void evaluates_complex_business_logic() {
        var filter = Parse(
            """
            stream = 'orders' AND
            properties.amount > 100 AND
            properties.region IN ('us-east', 'us-west') AND
            properties.customerType != 'internal'
            """
        );

        var matching = _faker.With(
            "orders",
            metadata: new Metadata()
                .With("amount", 150)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongAmount = _faker.With(
            "orders",
            metadata: new Metadata()
                .With("amount", 50)
                .With("region", "us-east")
                .With("customerType", "external")
        );

        var wrongRegion = _faker.With(
            "orders",
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

        var matchingCreated = _faker.With("orders", "OrderCreated");
        var matchingUpdated = _faker.With("orders", "OrderUpdated");
        var wrongStream     = _faker.With("payments", "OrderCreated");
        var wrongSchema     = _faker.With("orders", "OrderDeleted");

        filter.IsMatch(matchingCreated).ShouldBeTrue();
        filter.IsMatch(matchingUpdated).ShouldBeTrue();
        filter.IsMatch(wrongStream).ShouldBeFalse();
        filter.IsMatch(wrongSchema).ShouldBeFalse();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void handles_empty_string_comparison() {
        var filter      = Parse("properties.tag = ''");
        var matching    = _faker.With(metadata: new Metadata().With("tag", ""));
        var nonMatching = _faker.With(metadata: new Metadata().With("tag", "value"));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void handles_zero_in_numeric_comparison() {
        var filter      = Parse("position = 0");
        var matching    = _faker.With(position: 0);
        var nonMatching = _faker.With(position: 1);

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    [Test]
    public void handles_negative_numbers() {
        var filter      = Parse("properties.offset < 0");
        var matching    = _faker.With(metadata: new Metadata().With("offset", -10));
        var nonMatching = _faker.With(metadata: new Metadata().With("offset", 10));

        filter.IsMatch(matching).ShouldBeTrue();
        filter.IsMatch(nonMatching).ShouldBeFalse();
    }

    #endregion
}
