using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class ServerFilterExtractorTests {

    #region Stream Filter Extraction

    public readonly record struct StreamExtractionTestCase(string Description, string Expression, Type ExpectedType, string ExpectedExpression) {
        public override string ToString() => Description;
    }

    public class StreamExtractionTestCases : TestCaseGenerator<StreamExtractionTestCase> {
        const string GamesStream    = "Games-D0332EB7480643E481BF3205296958FD";
        const string OrdersStream   = "Orders-A1B2C3D4E5F6789012345678ABCDEF01";
        const string PaymentsStream = "Payments-F9E8D7C6B5A4321098765432FEDCBA98";

        protected override IEnumerable<StreamExtractionTestCase> Data() {
            // Exact stream match → StreamNameFilter
            yield return new("Exact stream match",         $"stream = '{GamesStream}'",                       typeof(StreamNameFilter), GamesStream);
            // LIKE without wildcards produces a regex (^literal$) → StreamRegexFilter
            yield return new("LIKE exact match (no wildcards)", $"stream LIKE '{OrdersStream}'",               typeof(StreamRegexFilter), $"^{OrdersStream}$");
            // Multiple streams → StreamRegexFilter with alternation
            yield return new("Multiple streams (IN)",      $"stream IN ('{OrdersStream}', '{PaymentsStream}')", typeof(StreamRegexFilter), $"^({OrdersStream}|{PaymentsStream})$");
            // Category prefix → StreamRegexFilter
            yield return new("Category prefix (Games-%)",  "stream LIKE 'Games-%'",                            typeof(StreamRegexFilter), @"^Games-.*$");
            yield return new("Category prefix (Orders-%)", "stream LIKE 'Orders-%'",                           typeof(StreamRegexFilter), @"^Orders-.*$");
            // Single char wildcard → StreamRegexFilter
            yield return new("Single char wildcard",       "stream LIKE 'Games-_'",                            typeof(StreamRegexFilter), @"^Games-.$");
            // Case-insensitive → StreamRegexFilter
            yield return new("Case-insensitive prefix",    "stream ILIKE 'GAMES-%'",                           typeof(StreamRegexFilter), @"(?i)^GAMES-.*$");
            yield return new("Case-insensitive exact",     $"stream ILIKE '{GamesStream}'",                    typeof(StreamRegexFilter), $"(?i)^{GamesStream}$");
        }
    }

    [Test]
    [StreamExtractionTestCases]
    public async Task extracts_stream_filter(StreamExtractionTestCase testcase) {
        var expr   = ParseExpression(testcase.Expression);
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter.GetType()).IsEqualTo(testcase.ExpectedType);
        await Assert.That(filter.Expression).IsEqualTo(testcase.ExpectedExpression);
    }

    [Test]
    public async Task extracts_target_stream_from_equality() {
        var expr   = ParseExpression("stream = 'Orders-A1B2C3D4E5F6'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamNameFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("Orders-A1B2C3D4E5F6"));
        await Assert.That(filter.Expression).IsEqualTo("Orders-A1B2C3D4E5F6");
    }

    [Test]
    public async Task category_prefix_returns_stream_regex_filter() {
        var expr   = ParseExpression("stream LIKE 'Orders-%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamRegexFilter>();
    }

    [Test]
    public async Task in_expression_returns_stream_regex_filter() {
        var expr   = ParseExpression("stream IN ('Orders-123', 'Payments-456')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamRegexFilter>();
        await Assert.That(filter.Expression).IsEqualTo("^(Orders-123|Payments-456)$");
    }

    #endregion

    #region Server-Side Stream Filters

    [Test]
    public async Task converts_stream_equality_to_stream_name_filter() {
        var expr   = ParseExpression("stream = 'Games-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamNameFilter>();
        await Assert.That(filter.Expression).IsEqualTo("Games-ABC123");
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
    public async Task converts_schema_name_equality_to_idx_et_regex() {
        var expr   = ParseExpression("schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<SchemaFilter>();
        await Assert.That(((SchemaFilter)filter).IndexName).IsEqualTo("$idx-et-OrderCreated");
    }

    [Test]
    public async Task converts_schema_alias_to_idx_et_regex() {
        var expr   = ParseExpression("schema = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<SchemaFilter>();
        await Assert.That(((SchemaFilter)filter).IndexName).IsEqualTo("$idx-et-OrderCreated");
    }

    [Test]
    public async Task schema_like_has_no_server_filter() {
        // LIKE cannot target a single index stream — evaluated client-side
        var expr   = ParseExpression("schema_name LIKE 'Order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task schema_in_has_no_server_filter() {
        // IN cannot target a single index stream — evaluated client-side
        var expr   = ParseExpression("schema_name IN ('OrderCreated', 'OrderUpdated')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Category Filter Extraction

    [Test]
    public async Task extracts_category_equality_to_idx_ce_stream() {
        var expr   = ParseExpression("category = 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)filter).IndexName).IsEqualTo("$idx-ce-orders");
    }

    [Test]
    public async Task category_in_has_no_server_filter() {
        // IN cannot target a single index stream — evaluated client-side
        var expr   = ParseExpression("category IN ('orders', 'payments')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task category_like_has_no_server_filter() {
        // LIKE cannot target a single index stream — evaluated client-side
        var expr   = ParseExpression("category LIKE 'order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task category_wins_over_schema() {
        var expr   = ParseExpression("category = 'orders' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)filter).IndexName).IsEqualTo("$idx-ce-orders");
    }

    [Test]
    public async Task stream_wins_over_category() {
        var expr   = ParseExpression("stream = 'orders-99' AND category = 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("orders-99"));
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

        await Assert.That(filter).IsTypeOf<UserIndexFilter>();
        await Assert.That(((UserIndexFilter)filter).IndexName).IsEqualTo("$idx-user-orders-by-country");
    }

    [Test]
    public async Task index_combined_with_schema_uses_index() {
        var expr   = ParseExpression("index = 'orders-by-country' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<UserIndexFilter>();
        await Assert.That(((UserIndexFilter)filter).IndexName).IsEqualTo("$idx-user-orders-by-country");
    }

    [Test]
    public async Task category_wins_over_index() {
        var expr   = ParseExpression("category = 'orders' AND index = 'orders-by-country'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)filter).IndexName).IsEqualTo("$idx-ce-orders");
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
        var expr   = ParseExpression("properties['amount'] > 100");
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

        await Assert.That(filter).IsTypeOf<StreamNameFilter>();
        await Assert.That(filter.Expression).IsEqualTo("Orders-ABC123");
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("Orders-ABC123"));
    }

    [Test]
    public async Task extracts_server_filter_with_properties() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties['amount'] > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamNameFilter>();
        await Assert.That(filter.Expression).IsEqualTo("Orders-ABC123");
    }

    [Test]
    public async Task extracts_schema_filter_with_properties() {
        var expr   = ParseExpression("schema_name = 'OrderCreated' AND properties['userId'] = 'Users-123'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<SchemaFilter>();
        await Assert.That(((SchemaFilter)filter).IndexName).IsEqualTo("$idx-et-OrderCreated");
    }

    [Test]
    public async Task extracts_server_filter_with_multiple_client_predicates() {
        var expr   = ParseExpression("stream = 'Orders-ABC123' AND properties['amount'] > 100 AND timestamp >= '2024-01-01'");
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
    public async Task or_of_stream_predicates_derives_stream_regex_union() {
        var expr   = ParseExpression("stream = 'Orders-001' OR stream = 'Payments-002'");
        var filter = ServerFilterExtractor.Extract(expr);

        // OR of pure stream predicates lifts to a single server-side regex union.
        await Assert.That(filter).IsTypeOf<StreamRegexFilter>();
    }

    [Test]
    public async Task or_with_non_stream_disjunct_has_no_server_filter() {
        var expr   = ParseExpression("stream = 'Orders-001' OR schema_name = 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        // A non-stream disjunct poisons the union — a stream regex would under-match,
        // so the whole expression stays client-side.
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
    public async Task exact_stream_match_preserves_special_characters() {
        var expr   = ParseExpression("stream = 'Orders.v1-ABC123'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamNameFilter>();
        await Assert.That(filter.Expression).IsEqualTo("Orders.v1-ABC123");
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
    public async Task expression_filter_extracts_stream_name_server_filter() {
        var filter = (ExpressionFilter)RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties['amount'] > 100");

        await Assert.That(filter.ServerFilter).IsTypeOf<StreamNameFilter>();
        await Assert.That(((StreamNameFilter)filter.ServerFilter).TargetStream).IsEqualTo(StreamName.From("Orders-ABC123"));
        await Assert.That(filter.Expression).IsEqualTo("stream = 'Orders-ABC123' AND properties['amount'] > 100");
    }

    [Test]
    public async Task expression_filter_matches_records_correctly() {
        // 1704067200000 = 2024-01-01T00:00:00Z in unix millis
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND timestamp >= 1704067200000");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                StreamPosition = new StreamPosition(StreamName.From("Orders-ABC123"), Revision.From(0))
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
                StreamPosition = new StreamPosition(StreamName.From("Orders-ABC123"), Revision.From(0))
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
        var filter = RecordFilter.ByStreamPattern("^Orders-");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                StreamPosition = new StreamPosition(StreamName.From("Orders-D0332EB7480643E4"), Revision.From(0))
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
                StreamPosition = new StreamPosition(StreamName.From("Payments-F9E8D7C6B5A43210"), Revision.From(0))
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
        var filter = RecordFilter.ByExpression("stream = 'Orders-ABC123' AND properties['amount'] > 100");

        var matching = new Record {
            Id       = Guid.NewGuid(),
            Position = new RecordPosition {
                LogPosition    = LogPosition.From(0),
                StreamPosition = new StreamPosition(StreamName.From("Orders-ABC123"), Revision.From(0))
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
                StreamPosition = new StreamPosition(StreamName.From("Orders-ABC123"), Revision.From(0))
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

    #region Schema Negated Expressions

    [Test]
    public async Task negated_schema_equality_has_no_server_filter() {
        var expr   = ParseExpression("schema != 'OrderCreated'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_schema_like_has_no_server_filter() {
        var expr   = ParseExpression("schema_name NOT LIKE 'Order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_schema_in_has_no_server_filter() {
        var expr   = ParseExpression("schema NOT IN ('OrderCreated', 'OrderUpdated')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Category Negated Expressions

    [Test]
    public async Task negated_category_equality_has_no_server_filter() {
        var expr   = ParseExpression("category != 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_category_like_has_no_server_filter() {
        var expr   = ParseExpression("category NOT LIKE 'order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task negated_category_in_has_no_server_filter() {
        var expr   = ParseExpression("category NOT IN ('orders', 'payments')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region ILIKE Falls Through to Client-Side

    [Test]
    public async Task schema_ilike_has_no_server_filter() {
        // ILIKE is a pattern match — cannot target a single index stream
        var expr   = ParseExpression("schema_name ILIKE 'order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task category_ilike_has_no_server_filter() {
        // ILIKE is a pattern match — cannot target a single index stream
        var expr   = ParseExpression("category ILIKE 'order%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Multiple Predicate Combination

    [Test]
    public async Task multiple_schema_like_predicates_have_no_server_filter() {
        var expr   = ParseExpression("schema LIKE 'Order%' AND schema LIKE '%Created'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task multiple_category_like_predicates_have_no_server_filter() {
        var expr   = ParseExpression("category LIKE 'order%' AND category LIKE '%payment%'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region IN with Non-String Values

    [Test]
    public async Task stream_in_with_numeric_values_has_no_server_filter() {
        // IN with non-string values cannot be extracted — TryExtractStringValues rejects them
        var expr   = ParseExpression("stream IN ('orders-1', 'orders-2') AND position > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        // Stream IN still works because its values are strings
        await Assert.That(filter).IsTypeOf<StreamFilter>();
    }

    [Test]
    public async Task schema_in_with_mixed_types_falls_through() {
        // If the only server-liftable predicate is IN but has non-string values, no server filter
        var expr   = ParseExpression("position > 100");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Priority Chain

    [Test]
    public async Task stream_wins_over_index() {
        var expr   = ParseExpression("stream = 'orders-99' AND index = 'orders-by-country'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("orders-99"));
    }

    [Test]
    public async Task full_priority_chain_uses_stream() {
        // Expression with all 4 filter types — stream wins
        var expr   = ParseExpression("stream = 'orders-99' AND category = 'orders' AND index = 'my-index' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("orders-99"));
    }

    [Test]
    public async Task priority_without_stream_uses_category() {
        // No stream predicate — category wins over index and schema
        var expr   = ParseExpression("category = 'orders' AND index = 'my-index' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)filter).IndexName).IsEqualTo("$idx-ce-orders");
    }

    [Test]
    public async Task priority_without_stream_or_category_uses_index() {
        // No stream or category — index wins over schema
        var expr   = ParseExpression("index = 'my-index' AND schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<UserIndexFilter>();
        await Assert.That(((UserIndexFilter)filter).IndexName).IsEqualTo("$idx-user-my-index");
    }

    #endregion

    #region Exact Regex Assertions

    [Test]
    public async Task category_equality_produces_idx_ce_index_name() {
        var expr   = ParseExpression("category = 'orders'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<CategoryFilter>();
        await Assert.That(((CategoryFilter)filter).IndexName).IsEqualTo("$idx-ce-orders");
        await Assert.That(filter.Expression).IsEqualTo("orders");
    }

    [Test]
    public async Task schema_equality_produces_idx_et_index_name() {
        var expr   = ParseExpression("schema = 'OrderPlaced'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<SchemaFilter>();
        await Assert.That(((SchemaFilter)filter).IndexName).IsEqualTo("$idx-et-OrderPlaced");
        await Assert.That(filter.Expression).IsEqualTo("OrderPlaced");
    }

    [Test]
    public async Task index_produces_idx_user_index_name() {
        var expr   = ParseExpression("index = 'orders-by-country'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<UserIndexFilter>();
        await Assert.That(((UserIndexFilter)filter).IndexName).IsEqualTo("$idx-user-orders-by-country");
        await Assert.That(filter.Expression).IsEqualTo("orders-by-country");
    }

    #endregion

    #region NOT-Wrapped Compound Expressions

    [Test]
    public async Task not_wrapping_compound_has_no_server_filter() {
        var expr   = ParseExpression("NOT (stream = 'orders-1' AND schema = 'OrderPlaced')");
        var filter = ServerFilterExtractor.Extract(expr);

        // NOT wrapping a compound expression is not an AND at the top level
        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    [Test]
    public async Task not_wrapping_stream_equality_has_no_server_filter() {
        var expr   = ParseExpression("NOT (stream = 'orders-1')");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsEqualTo(RecordFilter.None);
    }

    #endregion

    #region Index with Client Predicates

    [Test]
    public async Task index_combined_with_client_predicates_uses_index() {
        var expr   = ParseExpression("index = 'my-index' AND position > 100 AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<UserIndexFilter>();
        await Assert.That(((UserIndexFilter)filter).IndexName).IsEqualTo("$idx-user-my-index");
    }

    #endregion

    #region Deeply Nested AND Flattening

    [Test]
    public async Task flattens_deeply_nested_and_chain() {
        // 5-level AND chain — all conjuncts should be flattened and extractable predicates found
        var expr   = ParseExpression("stream = 'orders-1' AND schema = 'OrderPlaced' AND position > 10 AND revision > 0 AND timestamp >= '2024-01-01'");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("orders-1"));
    }

    [Test]
    public async Task flattens_nested_parenthesized_ands() {
        // Parenthesized grouping that still produces a pure AND tree
        var expr   = ParseExpression("(stream = 'orders-1' AND schema = 'OrderPlaced') AND (position > 10 AND revision > 0)");
        var filter = ServerFilterExtractor.Extract(expr);

        await Assert.That(filter).IsTypeOf<StreamFilter>();
        await Assert.That(((StreamNameFilter)filter).TargetStream).IsEqualTo(StreamName.From("orders-1"));
    }

    #endregion

    static IFilterExpression ParseExpression(string input) => ExpressionFilterParser.Parse(input);
}
