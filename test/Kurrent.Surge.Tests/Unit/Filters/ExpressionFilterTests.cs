using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ExpressionFilterTests {
    readonly RecordFaker _faker = new();

    static IFilterExpression Parse(string expression) => ExpressionFilterParser.Parse(expression);

    #region Comparison Operators

    [Test]
    public async Task evaluates_stream_equality() {
        var filter            = Parse("stream = 'orders'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("payments");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async Task evaluates_stream_equality_case_insensitive() {
        var filter = Parse("stream = 'ORDERS'");
        var record = _faker.With("orders");

        await Assert.That(filter.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task evaluates_not_equals() {
        var filter            = Parse("stream != 'test'");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async Task evaluates_greater_than() {
        var filter            = Parse("position > 100");
        var matchingRecord    = _faker.With(position: 150);
        var nonMatchingRecord = _faker.With(position: 50);

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async Task evaluates_less_than() {
        var filter            = Parse("position < 100");
        var matchingRecord    = _faker.With(position: 50);
        var nonMatchingRecord = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async Task evaluates_greater_than_or_equal() {
        var filter          = Parse("position >= 100");
        var matchingEqual   = _faker.With(position: 100);
        var matchingGreater = _faker.With(position: 150);
        var nonMatching     = _faker.With(position: 50);

        await Assert.That(filter.IsMatch(matchingEqual)).IsTrue();
        await Assert.That(filter.IsMatch(matchingGreater)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_less_than_or_equal() {
        var filter        = Parse("position <= 100");
        var matchingEqual = _faker.With(position: 100);
        var matchingLess  = _faker.With(position: 50);
        var nonMatching   = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matchingEqual)).IsTrue();
        await Assert.That(filter.IsMatch(matchingLess)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_timestamp_comparison() {
        var filter = Parse("timestamp >= 1704067200000"); // 2024-01-01T00:00:00Z in unix millis
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

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    #endregion

    #region Logical Operators

    [Test]
    public async Task evaluates_and_expression() {
        var filter         = Parse("stream = 'orders' AND schema_name = 'OrderCreated'");
        var matchingRecord = _faker.With("orders", "OrderCreated");
        var wrongStream    = _faker.With("payments", "OrderCreated");
        var wrongSchema    = _faker.With("orders", "OrderUpdated");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
    }

    [Test]
    public async Task evaluates_or_expression() {
        var filter         = Parse("stream = 'orders' OR stream = 'payments'");
        var ordersRecord   = _faker.With("orders");
        var paymentsRecord = _faker.With("payments");
        var usersRecord    = _faker.With("users");

        await Assert.That(filter.IsMatch(ordersRecord)).IsTrue();
        await Assert.That(filter.IsMatch(paymentsRecord)).IsTrue();
        await Assert.That(filter.IsMatch(usersRecord)).IsFalse();
        
        var properFilter = RecordFilter.ByExpression("stream = 'orders' OR stream = 'payments'");
        await Assert.That(properFilter.As<ExpressionFilter>().ServerFilter).IsTypeOf<StreamRegexFilter>();
    }

    [Test]
    public async Task evaluates_not_expression() {
        var filter            = Parse("NOT (stream = 'test')");
        var matchingRecord    = _faker.With("orders");
        var nonMatchingRecord = _faker.With("test");

        await Assert.That(filter.IsMatch(matchingRecord)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingRecord)).IsFalse();
    }

    [Test]
    public async Task evaluates_complex_logical_expression() {
        var filter           = Parse("(stream = 'orders' OR stream = 'payments') AND schema_name = 'Created'");
        var matchingOrders   = _faker.With("orders", "Created");
        var matchingPayments = _faker.With("payments", "Created");
        var wrongSchema      = _faker.With("orders", "Updated");
        var wrongStream      = _faker.With("users", "Created");

        await Assert.That(filter.IsMatch(matchingOrders)).IsTrue();
        await Assert.That(filter.IsMatch(matchingPayments)).IsTrue();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
    }

    [Test]
    public async Task evaluates_and_with_short_circuit() {
        var filter = Parse("stream = 'never' AND properties['missing'] > 100");
        var record = _faker.With("orders");

        await Assert.That(filter.IsMatch(record)).IsFalse();
    }

    [Test]
    public async Task evaluates_or_with_short_circuit() {
        var filter = Parse("stream = 'orders' OR properties['missing'] > 100");
        var record = _faker.With("orders");

        await Assert.That(filter.IsMatch(record)).IsTrue();
    }

    #endregion

    #region Pattern Matching

    [Test]
    public async Task evaluates_like_with_percent_wildcard() {
        var filter      = Parse("stream LIKE 'order%'");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("order");
        var matching3   = _faker.With("orders-123");
        var nonMatching = _faker.With("payments");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_like_with_underscore_wildcard() {
        var filter       = Parse("stream LIKE 'order_'");
        var matching     = _faker.With("orders");
        var nonMatching1 = _faker.With("order");
        var nonMatching2 = _faker.With("order-123");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching1)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatching2)).IsFalse();
    }

    [Test]
    public async Task evaluates_like_with_exact_match() {
        var filter       = Parse("stream LIKE 'orders-99'");
        var matching     = _faker.With("orders-99");
        var nonMatching1 = _faker.With("orders-");
        var nonMatching2 = _faker.With("orders-123");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching1)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatching2)).IsFalse();
    }

    [Test]
    public async Task evaluates_like_case_sensitive() {
        var filter      = Parse("stream LIKE 'Order%'");
        var matching    = _faker.With("Orders");
        var nonMatching = _faker.With("orders");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_ilike_case_insensitive() {
        var filter    = Parse("stream ILIKE 'order%'");
        var matching1 = _faker.With("ORDERS-123");
        var matching2 = _faker.With("Orders-456");
        var matching3 = _faker.With("orders-789");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
    }

    [Test]
    public async Task evaluates_not_like() {
        var filter      = Parse("stream NOT LIKE '%test%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("test-stream");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_not_ilike() {
        var filter      = Parse("stream NOT ILIKE '%TEST%'");
        var matching    = _faker.With("orders");
        var nonMatching = _faker.With("my-test-stream");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Null Checking

    [Test]
    public async Task evaluates_is_null_for_missing_property() {
        var filter            = Parse("properties['correlationId'] IS NULL");
        var recordWithoutProp = _faker.With();
        var recordWithProp    = _faker.With(metadata: new Metadata().With("correlationId", "abc"));

        await Assert.That(filter.IsMatch(recordWithoutProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithProp)).IsFalse();
    }

    [Test]
    public async Task evaluates_is_not_null() {
        var filter            = Parse("properties['userId'] IS NOT NULL");
        var recordWithProp    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = _faker.With();

        await Assert.That(filter.IsMatch(recordWithProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithoutProp)).IsFalse();
    }

    #endregion

    #region Set Membership

    [Test]
    public async Task evaluates_in_expression() {
        var filter      = Parse("stream IN ('orders', 'payments', 'refunds')");
        var matching1   = _faker.With("orders");
        var matching2   = _faker.With("payments");
        var matching3   = _faker.With("refunds");
        var nonMatching = _faker.With("users");

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(matching3)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_in_case_insensitive() {
        var filter   = Parse("stream IN ('ORDERS', 'PAYMENTS')");
        var matching = _faker.With("orders");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async Task evaluates_not_in_expression() {
        var filter      = Parse("schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')");
        var matching    = _faker.With(schemaName: "OrderCreated");
        var nonMatching = _faker.With(schemaName: "SystemEvent");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_in_with_numbers() {
        var filter      = Parse("position IN (100, 200, 300)");
        var matching    = _faker.With(position: 200);
        var nonMatching = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Range Operators

    [Test]
    public async Task evaluates_between_inclusive() {
        var filter           = Parse("position BETWEEN 100 AND 200");
        var matchingLower    = _faker.With(position: 100);
        var matchingMiddle   = _faker.With(position: 150);
        var matchingUpper    = _faker.With(position: 200);
        var nonMatchingBelow = _faker.With(position: 50);
        var nonMatchingAbove = _faker.With(position: 250);

        await Assert.That(filter.IsMatch(matchingLower)).IsTrue();
        await Assert.That(filter.IsMatch(matchingMiddle)).IsTrue();
        await Assert.That(filter.IsMatch(matchingUpper)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingBelow)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatchingAbove)).IsFalse();
    }

    [Test]
    public async Task evaluates_between_with_dates() {
        var filter = Parse("timestamp BETWEEN 1704067200000 AND 1735603200000"); // 2024-01-01 to 2024-12-31 in unix millis
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

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatchingBefore)).IsFalse();
        await Assert.That(filter.IsMatch(nonMatchingAfter)).IsFalse();
    }

    [Test]
    public async Task evaluates_not_between() {
        var filter      = Parse("position NOT BETWEEN 100 AND 200");
        var matching1   = _faker.With(position: 50);
        var matching2   = _faker.With(position: 250);
        var nonMatching = _faker.With(position: 150);

        await Assert.That(filter.IsMatch(matching1)).IsTrue();
        await Assert.That(filter.IsMatch(matching2)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Property Access

    [Test]
    public async Task evaluates_properties_bracket_notation() {
        var filter      = Parse("properties['userId'] = 'user-123'");
        var matching    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var nonMatching = _faker.With(metadata: new Metadata().With("userId", "user-456"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_properties_dot_notation() {
        var filter      = Parse("properties['amount'] > 100");
        var matching    = _faker.With(metadata: new Metadata().With("amount", 150));
        var nonMatching = _faker.With(metadata: new Metadata().With("amount", 50));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_props_alias() {
        var filter      = Parse("props['correlationId'] IS NOT NULL");
        var matching    = _faker.With(metadata: new Metadata().With("correlationId", "abc-123"));
        var nonMatching = _faker.With();

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_missing_property_as_null() {
        var filter = Parse("properties['missing'] = 'value'");
        var record = _faker.With();

        await Assert.That(filter.IsMatch(record)).IsFalse();
    }

    #endregion

    #region Type Coercion

    [Test]
    public async Task coerces_string_to_number_for_comparison() {
        var filter   = Parse("properties['amount'] > 100");
        var matching = _faker.With(metadata: new Metadata().With("amount", "150"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async Task coerces_number_to_string_for_comparison() {
        var filter   = Parse("properties['code'] = '42'");
        var matching = _faker.With(metadata: new Metadata().With("code", 42));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async Task handles_boolean_property_values() {
        var filter      = Parse("properties['active'] = true");
        var matching    = _faker.With(metadata: new Metadata().With("active", true));
        var nonMatching = _faker.With(metadata: new Metadata().With("active", false));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Complex Expressions

    [Test]
    public async Task evaluates_complex_business_logic() {
        var filter = Parse(
            """
            stream = 'orders' AND
            properties['amount'] > 100 AND
            properties['region'] IN ('us-east', 'us-west') AND
            properties['customerType'] != 'internal'
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

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(wrongAmount)).IsFalse();
        await Assert.That(filter.IsMatch(wrongRegion)).IsFalse();
    }

    [Test]
    public async Task evaluates_nested_or_with_and() {
        var filter = Parse("(schema_name = 'OrderCreated' OR schema_name = 'OrderUpdated') AND stream = 'orders'");

        var matchingCreated = _faker.With("orders", "OrderCreated");
        var matchingUpdated = _faker.With("orders", "OrderUpdated");
        var wrongStream     = _faker.With("payments", "OrderCreated");
        var wrongSchema     = _faker.With("orders", "OrderDeleted");

        await Assert.That(filter.IsMatch(matchingCreated)).IsTrue();
        await Assert.That(filter.IsMatch(matchingUpdated)).IsTrue();
        await Assert.That(filter.IsMatch(wrongStream)).IsFalse();
        await Assert.That(filter.IsMatch(wrongSchema)).IsFalse();
    }

    #endregion

    #region Column Aliases

    [Test]
    public async Task evaluates_event_type_alias() {
        var filter      = Parse("schema_name = 'OrderPlaced'");
        var matching    = _faker.With(schemaName: "OrderPlaced");
        var nonMatching = _faker.With(schemaName: "OrderShipped");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_event_number_alias() {
        var filter      = Parse("revision > 5");
        var matching    = _faker.With(streamRevision: 10);
        var nonMatching = _faker.With(streamRevision: 3);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_log_position_alias() {
        var filter      = Parse("log_position > 1000");
        var matching    = _faker.With(position: 1500);
        var nonMatching = _faker.With(position: 500);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_created_at_alias() {
        var filter = Parse("created_at >= 1767225600000"); // 2026-01-01 in unix millis
        var matching = _faker.With(
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        var nonMatching = _faker.With(
            timestamp: new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_ts_alias() {
        var filter = Parse("ts >= 1767225600000"); // 2026-01-01 in unix millis
        var matching = _faker.With(
            timestamp: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        var nonMatching = _faker.With(
            timestamp: new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        );

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_metadata_alias() {
        var filter      = Parse("metadata['region'] = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_stream_name_alias() {
        var filter      = Parse("stream = 'orders-99'");
        var matching    = _faker.With("orders-99");
        var nonMatching = _faker.With("payments-1");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Index Virtual Column

    [Test]
    public async Task evaluates_index_always_true() {
        var filter = Parse("index = 'orders-by-country'");
        var record = _faker.With("anything-123");

        await Assert.That(filter.IsMatch(record)).IsTrue();
    }

    [Test]
    public async Task evaluates_index_combined_with_schema() {
        var filter      = Parse("index = 'orders-by-country' AND schema = 'OrderPlaced'");
        var matching    = _faker.With(schemaName: "OrderPlaced");
        var nonMatching = _faker.With(schemaName: "OrderShipped");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Property Access via Dot-Path Fallback

    [Test]
    public async Task evaluates_metadata_dot_path_key_access() {
        var filter      = Parse("properties['region'] = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Category Derivation

    [Test]
    public async Task evaluates_category_from_stream_with_delimiter() {
        var filter      = Parse("category = 'order'");
        var matching    = _faker.With("order-abc123");
        var nonMatching = _faker.With("payment-2026-001");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_category_from_stream_with_multiple_delimiters() {
        var filter   = Parse("category = 'payment'");
        var matching = _faker.With("payment-2026-001");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
    }

    [Test]
    public async Task evaluates_category_from_stream_without_delimiter() {
        var filter      = Parse("category = 'nodelimiter'");
        var matching    = _faker.With("nodelimiter");
        var nonMatching = _faker.With("other");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Field Prefix Stripping

    [Test]
    public async Task evaluates_field_stream_alias() {
        var filter      = Parse("field_stream = 'orders-99'");
        var matching    = _faker.With("orders-99");
        var nonMatching = _faker.With("payments-1");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_field_category_alias() {
        var filter      = Parse("field_category = 'orders'");
        var matching    = _faker.With("orders-abc123");
        var nonMatching = _faker.With("payments-1");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_field_schema_alias() {
        var filter      = Parse("field_schema = 'OrderPlaced'");
        var matching    = _faker.With(schemaName: "OrderPlaced");
        var nonMatching = _faker.With(schemaName: "OrderShipped");

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region . JSON Access Notation .

    [Test]
    public async Task evaluates_nested_dot_notation() {
        var filter      = Parse("metadata.region = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_arrow_text_string_comparison() {
        var filter      = Parse("properties->>'region' = 'us-east'");
        var matching    = _faker.With(metadata: new Metadata().With("region", "us-east"));
        var nonMatching = _faker.With(metadata: new Metadata().With("region", "eu-west"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task evaluates_arrow_is_not_null() {
        var filter            = Parse("properties->>'userId' IS NOT NULL");
        var recordWithProp    = _faker.With(metadata: new Metadata().With("userId", "user-123"));
        var recordWithoutProp = _faker.With();

        await Assert.That(filter.IsMatch(recordWithProp)).IsTrue();
        await Assert.That(filter.IsMatch(recordWithoutProp)).IsFalse();
    }

    [Test]
    public async Task evaluates_arrow_numeric_comparison() {
        var filter      = Parse("properties->>'amount' > 100");
        var matching    = _faker.With(metadata: new Metadata().With("amount", "150"));
        var nonMatching = _faker.With(metadata: new Metadata().With("amount", "50"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task handles_empty_string_comparison() {
        var filter      = Parse("properties['tag'] = ''");
        var matching    = _faker.With(metadata: new Metadata().With("tag", ""));
        var nonMatching = _faker.With(metadata: new Metadata().With("tag", "value"));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task handles_zero_in_numeric_comparison() {
        var filter      = Parse("position = 0");
        var matching    = _faker.With(position: 0);
        var nonMatching = _faker.With(position: 1);

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task handles_negative_numbers() {
        var filter      = Parse("properties['offset'] < 0");
        var matching    = _faker.With(metadata: new Metadata().With("offset", -10));
        var nonMatching = _faker.With(metadata: new Metadata().With("offset", 10));

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion
}
