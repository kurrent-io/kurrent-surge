using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
public class QueryExpressionNormalizerTests {
    readonly QueryExpressionNormalizer _normalizer = new(new NormalizerOptions {
        ColumnReplacements = new KeyValuePair<string, string>[] {
            new("schema",   "event_type"),
            new("revision", "event_number"),
            new("position", "log_position"),
        }.ToImmutableArray(),
        TableReplacements = new KeyValuePair<string, string>[] {
            new("records",    "kdb_log"),
            new("all_events", "kdb_log"),
        }.ToImmutableArray(),
        ForbiddenTablePrefix = "kdb_"
    });

    static QueryExpressionNormalizer CreateNormalizer(
        IEnumerable<KeyValuePair<string, string>>? columnReplacements = null,
        IEnumerable<KeyValuePair<string, string>>? tableReplacements = null,
        string? forbiddenTablePrefix = null
    ) {
        return new QueryExpressionNormalizer(new NormalizerOptions {
            ColumnReplacements   = (columnReplacements ?? []).ToImmutableArray(),
            TableReplacements    = (tableReplacements ?? []).ToImmutableArray(),
            ForbiddenTablePrefix = forbiddenTablePrefix
        });
    }

    static (bool IsValid, string? Value, string? Error) Normalize(
        QueryExpressionNormalizer normalizer,
        string input
    ) {
        var buffer = new char[input.Length * 2 + 1];
        var result = normalizer.Normalize(input.AsSpan(), buffer.AsSpan());
        return (result.IsValid, result.IsValid ? result.Value.ToString() : null, result.Error);
    }

    #region Passthrough

    [Test]
    [Arguments("select 1")]
    [Arguments("select a, b from t where x = 1")]
    [Arguments("")]
    public async Task copies_input_verbatim_when_no_replacements_match(string input) {
        var empty  = CreateNormalizer();
        var result = Normalize(empty, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(input);
    }

    #endregion

    #region Skip Regions

    [Test]
    [Arguments("-- schema records\nselect 1",           "-- schema records\nselect 1")]
    [Arguments("select /* schema */ 1",                  "select /* schema */ 1")]
    [Arguments("select /* /* nested */ still */ 1",      "select /* /* nested */ still */ 1")]
    [Arguments("select 'schema'",                        "select 'schema'")]
    [Arguments("select 'it''s'",                         "select 'it''s'")]
    [Arguments("select E'schema\\n'",                    "select E'schema\\n'")]
    [Arguments("select e'test\\''",                      "select e'test\\''")]
    [Arguments("select $$schema records$$",              "select $$schema records$$")]
    [Arguments("select $tag$schema$tag$",                "select $tag$schema$tag$")]
    [Arguments("select arr[1], list[2:3]",               "select arr[1], list[2:3]")]
    public async Task skips_region_without_replacement(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    #endregion

    #region Column Replacement

    [Test]
    [Arguments("select schema",                "select event_type")]
    [Arguments("select revision",              "select event_number")]
    [Arguments("select schema, revision",      "select event_type, event_number")]
    [Arguments("select SCHEMA",                "select event_type")]
    [Arguments("select unknown_col",           "select unknown_col")]
    [Arguments("where revision = 234",         "where event_number = 234")]
    [Arguments("select position as p",         "select log_position as p")]
    public async Task replaces_columns_in_default_context(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    #endregion

    #region Table Replacement

    [Test]
    [Arguments("from records",                          "from kdb_log")]
    [Arguments("FROM records",                          "FROM kdb_log")]
    [Arguments("from all_events",                       "from kdb_log")]
    [Arguments("from unknown_table",                    "from unknown_table")]
    [Arguments("select schema from records",            "select event_type from kdb_log")]
    [Arguments("from records where revision = 1",       "from kdb_log where event_number = 1")]
    [Arguments("join records on x = y",                 "join kdb_log on x = y")]
    [Arguments("left join records on x = y",            "left join kdb_log on x = y")]
    [Arguments("left outer join records on x = y",      "left outer join kdb_log on x = y")]
    [Arguments("from records, all_events",              "from kdb_log, kdb_log")]
    public async Task replaces_tables_in_table_position(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("select records from t",                 "select records from t")]
    [Arguments("where records = 1",                     "where records = 1")]
    public async Task does_not_replace_table_names_in_column_position(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task does_not_replace_column_names_in_table_position() {
        var result = Normalize(_normalizer, "from schema");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo("from schema");
    }

    #endregion

    #region Alias Preservation

    [Test]
    [Arguments("from records as r",             "from kdb_log as r")]
    [Arguments("from records AS r",             "from kdb_log AS r")]
    [Arguments("from records r",                "from kdb_log r")]
    [Arguments("from records schema",           "from kdb_log schema")]
    [Arguments("from records r where r.revision = 1",
               "from kdb_log r where r.event_number = 1")]
    public async Task preserves_table_aliases(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task preserves_column_alias_after_as() {
        var result = Normalize(_normalizer, "select schema as s");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo("select event_type as s");
    }

    #endregion

    #region Forbidden Prefix

    [Test]
    [Arguments("from kdb_internal")]
    [Arguments("from KDB_INTERNAL")]
    [Arguments("join kdb_lookup on x = y")]
    [Arguments("left join kdb_lookup on x = y")]
    [Arguments("from a, kdb_internal")]
    [Arguments("insert into kdb_sink")]
    [Arguments("update kdb_target set x = 1")]
    public async Task rejects_table_with_forbidden_prefix(string input) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    [Test]
    [Arguments("select kdb_col from t")]
    [Arguments("where kdb_col = 1")]
    [Arguments("order by kdb_col")]
    public async Task allows_forbidden_prefix_in_column_position(string input) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task allows_any_table_when_prefix_not_configured() {
        var noPrefix = CreateNormalizer();
        var result   = Normalize(noPrefix, "from kdb_internal");

        await Assert.That(result.IsValid).IsTrue();
    }

    #endregion

    #region Colon Alias

    [Test]
    [Arguments("select s: schema",                       "select s: event_type")]
    [Arguments("select s: schema, p: position",          "select s: event_type, p: log_position")]
    [Arguments("select s: schema, position as p",        "select s: event_type, log_position as p")]
    public async Task handles_colon_alias_in_select(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task handles_colon_alias_in_from() {
        var result = Normalize(_normalizer, "from r: records");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo("from r: kdb_log");
    }

    [Test]
    public async Task rejects_forbidden_prefix_via_colon_alias_in_from() {
        var result = Normalize(_normalizer, "from alias: kdb_internal");

        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion

    #region Cast Operator

    [Test]
    [Arguments("select schema::varchar",               "select event_type::varchar")]
    [Arguments("select revision::integer",             "select event_number::integer")]
    [Arguments("select x::schema",                     "select x::schema")]
    public async Task preserves_type_name_after_cast(string input, string expected) {
        var result = Normalize(_normalizer, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test]
    public async Task does_not_confuse_cast_with_colon_alias() {
        var result = Normalize(_normalizer, "select schema::text");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo("select event_type::text");
    }

    #endregion
}
