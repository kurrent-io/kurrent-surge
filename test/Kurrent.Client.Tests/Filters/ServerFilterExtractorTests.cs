using Kurrent.Client.Streams;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ServerFilterExtractorTests {
    readonly ExpressionFilterParser _parser = new();

    #region Stream Filter Extraction

    public readonly record struct StreamExtractionTestCase(string Description, string Expression, IRecordFilter ExpectedFilter) {
        public override string ToString() => Description;
    }

    public class StreamExtractionTestCases : TestCaseGenerator<StreamExtractionTestCase> {
        // Realistic stream names following KurrentDB convention: {Category}-{Id}
        const string GamesStream    = "Games-D0332EB7480643E481BF3205296958FD";
        const string OrdersStream   = "Orders-A1B2C3D4E5F6789012345678ABCDEF01";
        const string PaymentsStream = "Payments-F9E8D7C6B5A4321098765432FEDCBA98";

        protected override IEnumerable<StreamExtractionTestCase> Data() {
            // Exact stream match - targets specific stream
            yield return new(
                "Exact stream match",
                $"stream = '{GamesStream}'",
                RecordFilter.ByStream(GamesStream)
            );

            // LIKE without wildcards - equivalent to exact match
            yield return new(
                "LIKE exact match (no wildcards)",
                $"stream LIKE '{OrdersStream}'",
                RecordFilter.ByStream(OrdersStream)
            );

            // Multiple streams - reads from $all with filter
            yield return new(
                "Multiple streams (IN)",
                $"stream IN ('{OrdersStream}', '{PaymentsStream}')",
                RecordFilter.ByStream(OrdersStream, PaymentsStream)
            );

            // Category prefix - common pattern for filtering by aggregate type
            yield return new(
                "Category prefix (Games-%)",
                "stream LIKE 'Games-%'",
                RecordFilter.ByStreamPattern(@"^Games-.*$")
            );

            // Category prefix - Orders
            yield return new(
                "Category prefix (Orders-%)",
                "stream LIKE 'Orders-%'",
                RecordFilter.ByStreamPattern(@"^Orders-.*$")
            );

            // Single char wildcard - matches exactly one character
            yield return new(
                "Single char wildcard",
                "stream LIKE 'Games-_'",
                RecordFilter.ByStreamPattern(@"^Games-.$")
            );

            // Case-insensitive category prefix
            yield return new(
                "Case-insensitive category prefix",
                "stream ILIKE 'GAMES-%'",
                RecordFilter.ByStreamPattern(@"(?i)^GAMES-.*$")
            );

            // Case-insensitive exact match - no target stream (could match multiple variants)
            yield return new(
                "Case-insensitive exact match",
                $"stream ILIKE '{GamesStream}'",
                RecordFilter.ByStreamPattern($"(?i)^{GamesStream}$")
            );
        }
    }

    [Test]
    [StreamExtractionTestCases]
    public async Task extracts_stream_filter(StreamExtractionTestCase testcase) {
        var expr   = ParseExpression(testcase.Expression);
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter).IsDeepEquivalentTo(testcase.ExpectedFilter);
    }

    [Test]
    public async Task extracts_target_stream_from_equality() {
        var expr   = ParseExpression("stream = 'Orders-A1B2C3D4E5F6'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("Orders-A1B2C3D4E5F6"));
        await Assert.That(filter.Expression).IsEqualTo("^Orders-A1B2C3D4E5F6$");
    }

    [Test]
    public async Task no_target_stream_for_category_prefix() {
        var expr   = ParseExpression("stream LIKE 'Orders-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task no_target_stream_for_in_expression() {
        var expr   = ParseExpression("stream IN ('Orders-123', 'Payments-456')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.All);
        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).IsEqualTo("^(Orders-123|Payments-456)$");
    }

    #endregion

    #region Server-Side Stream Filters

    [Test]
    public async Task converts_stream_equality_to_exact_regex() {
        var expr   = ParseExpression("stream = 'Games-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^Games-ABC123$");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task converts_category_prefix_to_regex() {
        var expr   = ParseExpression("stream LIKE 'Orders-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^Orders-.*$");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task converts_stream_like_suffix_to_regex() {
        var expr   = ParseExpression("stream LIKE '%-completed'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^.*-completed$");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task converts_stream_like_contains_to_regex() {
        var expr   = ParseExpression("stream LIKE '%order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^.*order.*$");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task converts_stream_in_to_alternation_regex() {
        var expr   = ParseExpression("stream IN ('Orders-001', 'Payments-002', 'Refunds-003')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^(Orders-001|Payments-002|Refunds-003)$");
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    #endregion

    #region Server-Side Schema Filters

    [Test]
    public async Task converts_schema_name_equality_to_exact_regex() {
        var expr   = ParseExpression("schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^OrderCreated$");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task converts_schema_alias_to_exact_regex() {
        var expr   = ParseExpression("schema = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^OrderCreated$");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task converts_schema_like_to_regex() {
        var expr   = ParseExpression("schema_name LIKE 'Order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^Order.*$");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task converts_schema_in_to_alternation_regex() {
        var expr   = ParseExpression("schema_name IN ('OrderCreated', 'OrderUpdated')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.Expression).IsEqualTo("^(OrderCreated|OrderUpdated)$");
        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    #endregion

    #region Category Filter Extraction

    [Test]
    public async Task extracts_category_equality_to_idx_ce_stream() {
        var expr   = ParseExpression("category = 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-ce-orders"));
    }

    [Test]
    public async Task extracts_category_in_to_idx_ce_streams() {
        var expr   = ParseExpression("category IN ('orders', 'payments')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).Contains("\\$idx-ce-orders");
        await Assert.That(filter.Expression).Contains("\\$idx-ce-payments");
    }

    [Test]
    public async Task extracts_category_like_to_idx_ce_pattern() {
        var expr   = ParseExpression("category LIKE 'order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).Contains("\\$idx-ce-order");
    }

    [Test]
    public async Task category_wins_over_schema() {
        var expr   = ParseExpression("category = 'orders' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-ce-orders"));
    }

    [Test]
    public async Task stream_wins_over_category() {
        var expr   = ParseExpression("stream = 'orders-99' AND category = 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("orders-99"));
    }

    [Test]
    public async Task category_or_schema_has_no_server_filter() {
        var expr   = ParseExpression("category = 'orders' OR schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Index Filter Extraction

    [Test]
    public async Task extracts_index_to_idx_user_stream() {
        var expr   = ParseExpression("index = 'orders-by-country'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-user-orders-by-country"));
    }

    [Test]
    public async Task index_combined_with_schema_uses_index() {
        var expr   = ParseExpression("index = 'orders-by-country' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-user-orders-by-country"));
    }

    [Test]
    public async Task category_wins_over_index() {
        var expr   = ParseExpression("category = 'orders' AND index = 'orders-by-country'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("$idx-ce-orders"));
    }

    #endregion

    #region Client-Side Filters (no server filter)

    [Test]
    public async Task timestamp_filter_has_no_server_filter() {
        var expr   = ParseExpression("timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task position_filter_has_no_server_filter() {
        var expr   = ParseExpression("position > 1000");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task properties_filter_has_no_server_filter() {
        var expr   = ParseExpression("properties.amount > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task revision_filter_has_no_server_filter() {
        var expr   = ParseExpression("stream_revision > 10");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Combined Server and Client Filters

    [Test]
    public async Task extracts_server_filter_from_combined_expression() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).IsEqualTo("^Orders-ABC123$");
        await Assert.That(filter.TargetStream).IsEqualTo(StreamName.From("Orders-ABC123"));
    }

    [Test]
    public async Task extracts_server_filter_with_properties() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties.amount > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(filter.Expression).IsEqualTo("^Orders-ABC123$");
    }

    [Test]
    public async Task extracts_schema_filter_with_properties() {
        var expr   = ParseExpression("schema_name = 'OrderCreated' AND properties.userId = 'Users-123'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<SchemaFilter>();
    }

    [Test]
    public async Task extracts_server_filter_with_multiple_client_predicates() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties.amount > 100 AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    #endregion

    #region Negated Expressions

    [Test]
    public async Task negated_stream_filter_has_no_server_filter() {
        var expr   = ParseExpression("stream != 'Test-123'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Negated comparisons cannot be lifted to server
        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_like_has_no_server_filter() {
        var expr   = ParseExpression("stream NOT LIKE 'Test-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_in_has_no_server_filter() {
        var expr   = ParseExpression("stream NOT IN ('Test-001', 'Demo-002')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region OR Expressions

    [Test]
    public async Task or_expression_has_no_server_filter() {
        var expr   = ParseExpression("stream = 'Orders-001' OR stream = 'Payments-002'");
        var filter = ServerFilterExtractor.Extract(expr);

        // OR expressions cannot be lifted to server as a single AND-flattened predicate
        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Mixed Scope Filters

    [Test]
    public async Task stream_and_schema_filters_uses_stream_scope() {
        // When we have both stream and schema_name filters, stream takes priority
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Stream predicate goes to server (priority), schema is handled client-side
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task client_only_expression_has_no_server_filter() {
        // Create a simple expression that doesn't match any server patterns
        var expr   = ParseExpression("position > 0");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task escapes_special_regex_characters() {
        var expr   = ParseExpression("stream = 'Orders.v1-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        // The dot should be escaped in the regex, but '-' doesn't need escaping
        await Assert.That(filter.Expression).IsEqualTo(@"^Orders\.v1-ABC123$");
    }

    [Test]
    public async Task multiple_category_predicates_combine() {
        // When we have multiple stream predicates in AND, they combine
        var expr   = ParseExpression("stream LIKE 'Orders-%' AND stream LIKE '%-completed'");
        var filter = ServerFilterExtractor.Extract(expr);

        // Both predicates can go to server and should be combined
        await Assert.That(filter).IsTypeOf<StreamFilter>();
        // Regex should be an alternation of both patterns
        await Assert.That(filter.Expression).IsNotNull();
        await Assert.That(filter.Expression).Contains("Orders");
        await Assert.That(filter.Expression).Contains("completed");
    }

    #endregion

    #region RecordFilter Integration

    [Test]
    public async Task expression_filter_extracts_target_stream() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties.amount > 100");

        await Assert.That(filter.TargetStream.Value).IsEqualTo("Orders-ABC123");
        await Assert.That(filter.Expression).IsEqualTo("stream = 'Orders-ABC123' AND properties.amount > 100");
    }

    [Test]
    public async Task expression_filter_matches_records_correctly() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND timestamp >= '2024-01-01'");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = new DateTime(
                2024, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            ),
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        var nonMatching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = new DateTime(
                2023, 6, 15,
                0, 0, 0,
                DateTimeKind.Utc
            ),
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task stream_filter_matches_category_prefix() {
        var filter = RecordFilter.ByStreamPrefix("Orders-");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-D0332EB7480643E4"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        var nonMatching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Payments-F9E8D7C6B5A43210"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
        };

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    [Test]
    public async Task expression_filter_with_client_predicate_matches() {
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties.amount > 100");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
                .With("amount", 150)
        };

        var nonMatching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                Stream         = StreamName.From("Orders-ABC123"),
                Revision = Revision.From(0)
            },
            Timestamp = DateTime.UtcNow,
            Metadata = new Metadata()
                .With(SystemMetadataKeys.SchemaName, "TestEvent")
                .With(SystemMetadataKeys.SchemaFormat, "json")
                .With("amount", 50)
        };

        await Assert.That(filter.IsMatch(matching)).IsTrue();
        await Assert.That(filter.IsMatch(nonMatching)).IsFalse();
    }

    #endregion

    IFilterExpression ParseExpression(string input) {
        var result = _parser.Parse(input);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to parse: {input}");

        return result.Value;
    }
}
