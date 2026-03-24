using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

#if NET9_0_OR_GREATER

public sealed record NormalizerTestCase(int Id, string Query, string Expected) {
    public override string ToString() => $"#{Id}";
}

static class NormalizerTestCases {
    public static readonly NormalizerOptions Options = new() {
        ColumnReplacements = new Dictionary<string, string> {
            ["schema"]    = "event_type",
            ["revision"]  = "event_number",
            ["position"]  = "log_position",
            ["timestamp"] = "created",
            ["data"]      = "payload",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        TableReplacements = new Dictionary<string, string> {
            ["records"]    = "kdb_log",
            ["all_events"] = "kdb_log",
            ["events"]     = "kdb_events",
            ["kdb.log"]    = "kdb_log",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        ForbiddenTablePrefix = "kdb_"
    };

    public static readonly NormalizerOptions NoPrefixOptions = new() {
        ColumnReplacements = Options.ColumnReplacements,
        TableReplacements  = Options.TableReplacements,
    };

    #region Success cases

    public static IEnumerable<NormalizerTestCase> SuccessCases() {
        // --- Column replacement ---
        yield return new( 1, "select schema",                "select event_type");
        yield return new( 2, "select revision",              "select event_number");
        yield return new( 3, "select schema, revision",      "select event_type, event_number");
        yield return new( 4, "select SCHEMA",                "select event_type");
        yield return new( 5, "select unknown_col",           "select unknown_col");
        yield return new( 6, "where revision = 234",         "where event_number = 234");
        yield return new( 7, "select position as p",         "select log_position as p");

        // --- Table replacement ---
        yield return new(10, "from records",                          "from kdb_log");
        yield return new(11, "FROM records",                          "FROM kdb_log");
        yield return new(12, "from all_events",                       "from kdb_log");
        yield return new(13, "from unknown_table",                    "from unknown_table");
        yield return new(14, "select schema from records",            "select event_type from kdb_log");
        yield return new(15, "from records where revision = 1",       "from kdb_log where event_number = 1");
        yield return new(16, "join records on x = y",                 "join kdb_log on x = y");
        yield return new(17, "left join records on x = y",            "left join kdb_log on x = y");
        yield return new(18, "left outer join records on x = y",      "left outer join kdb_log on x = y");
        yield return new(19, "from records, all_events",              "from kdb_log, kdb_log");
        yield return new(20, "select records from t",                 "select records from t");
        yield return new(21, "where records = 1",                     "where records = 1");
        yield return new(22, "from schema",                           "from schema");

        // --- Alias preservation ---
        yield return new(30, "from records as r",                     "from kdb_log as r");
        yield return new(31, "from records AS r",                     "from kdb_log AS r");
        yield return new(32, "from records r",                        "from kdb_log r");
        yield return new(33, "from records schema",                   "from kdb_log schema");
        yield return new(34, "from records r where r.revision = 1",   "from kdb_log r where r.event_number = 1");
        yield return new(35, "select schema as s",                    "select event_type as s");

        // --- Colon alias ---
        yield return new(40, "select s: schema",                      "select s: event_type");
        yield return new(41, "select s: schema, p: position",         "select s: event_type, p: log_position");
        yield return new(42, "select s: schema, position as p",       "select s: event_type, log_position as p");
        yield return new(43, "from r: records",                       "from r: kdb_log");

        // --- Qualified references ---
        yield return new(50, "where r.revision = 1",                  "where r.event_number = 1");
        yield return new(51, "select r.schema, r.position",           "select r.event_type, r.log_position");
        yield return new(52, "where t.unknown_col = 1",               "where t.unknown_col = 1");
        yield return new(53, "from main.records",                     "from main.kdb_log");

        // --- Double-quoted identifiers ---
        yield return new(60, "select \"schema\"",                     "select \"event_type\"");
        yield return new(61, "from \"records\"",                      "from \"kdb_log\"");
        yield return new(62, "where \"revision\" = 1",                "where \"event_number\" = 1");
        yield return new(63, "select \"unknown\"",                    "select \"unknown\"");
        yield return new(64, "select \"my\"\"col\"",                  "select \"my\"\"col\"");

        // --- Cast operator ---
        yield return new(70, "select schema::varchar",                "select event_type::varchar");
        yield return new(71, "select revision::integer",              "select event_number::integer");
        yield return new(72, "select x::schema",                      "select x::schema");
        yield return new(73, "select schema::text",                   "select event_type::text");

        // --- CTEs ---
        yield return new(80, "with schema as (select 1) select * from schema",
                              "with schema as (select 1) select * from schema");
        yield return new(81, "with a as (select 1), b as (select 2) select * from a",
                              "with a as (select 1), b as (select 2) select * from a");
        yield return new(82, "with cte as (select schema from records) select * from cte",
                              "with cte as (select event_type from kdb_log) select * from cte");
        yield return new(83, "with recursive cte as (select schema from records) select * from cte",
                              "with recursive cte as (select event_type from kdb_log) select * from cte");

        // --- Lambda ---
        yield return new(85, "select list_filter(l, lambda x: x > 1)",
                              "select list_filter(l, lambda x: x > 1)");
        yield return new(86, "select list_filter(l, lambda x: schema > 1)",
                              "select list_filter(l, lambda x: event_type > 1)");

        // --- Dollar parameters ---
        yield return new(88, "select $1",          "select $1");
        yield return new(89, "where $my_param = 1", "where $my_param = 1");

        // --- Parenthesis / subquery ---
        yield return new(90, "from (select schema from records) as t",
                              "from (select event_type from kdb_log) as t");

        // --- Numeric underscore ---
        yield return new(91, "where x = 100_000",  "where x = 100_000");

        // --- Skip regions (identifiers inside protected syntax are not replaced) ---
        yield return new(92, "-- schema records\nselect 1",           "-- schema records\nselect 1");
        yield return new(93, "select /* schema */ 1",                  "select /* schema */ 1");
        yield return new(94, "select /* /* nested */ still */ 1",      "select /* /* nested */ still */ 1");
        yield return new(95, "select 'schema'",                        "select 'schema'");
        yield return new(96, "select 'it''s'",                         "select 'it''s'");
        yield return new(97, "select E'schema\\n'",                    "select E'schema\\n'");
        yield return new(98, "select e'test\\''",                      "select e'test\\''");
        yield return new(99, "select $$schema records$$",              "select $$schema records$$");
        yield return new(100, "select $tag$schema$tag$",               "select $tag$schema$tag$");
        yield return new(101, "select arr[1], list[2:3]",              "select arr[1], list[2:3]");

        // --- Forbidden prefix allowed in non-table positions ---
        yield return new(102, "select kdb_col from t",                 "select kdb_col from t");
        yield return new(103, "where kdb_col = 1",                     "where kdb_col = 1");
        yield return new(104, "order by kdb_col",                      "order by kdb_col");
        yield return new(105, "-- from kdb_internal\nselect 1",        "-- from kdb_internal\nselect 1");
        yield return new(106, "/* from kdb_internal */ select 1",      "/* from kdb_internal */ select 1");
        yield return new(107, "where name = 'kdb_internal'",           "where name = 'kdb_internal'");
        yield return new(108, "where name = E'kdb_internal'",          "where name = E'kdb_internal'");
        yield return new(109, "where name = $$kdb_internal$$",         "where name = $$kdb_internal$$");

        // --- End-to-end ---
        yield return new(110, "select schema\nfrom records\nwhere revision = 234",
                               "select event_type\nfrom kdb_log\nwhere event_number = 234");
        yield return new(111, "select\n    s: schema,\n    position as p\nfrom records rec\nwhere rec.revision = 234",
                               "select\n    s: event_type,\n    log_position as p\nfrom kdb_log rec\nwhere rec.event_number = 234");

        // --- Roman's query ---
        yield return new(200,
            "SELECT \"kdb.log\" AS \"kdb.log\" FROM (SELECT \"kdb.log\" AS \"kdb.log\" FROM \"kdb.log\" WHERE \"kdb.log\"=\"kdb.log\") WHERE \"kdb.log\"=\"kdb.log\"",
            "SELECT \"kdb.log\" AS \"kdb.log\" FROM (SELECT \"kdb.log\" AS \"kdb.log\" FROM \"kdb_log\" WHERE \"kdb.log\"=\"kdb.log\") WHERE \"kdb.log\"=\"kdb.log\"");
    }

    #endregion

    #region Error cases

    public static IEnumerable<NormalizerTestCase> ErrorCases() {
        // --- Forbidden table prefix ---
        yield return new(300, "from kdb_internal",                     "");
        yield return new(301, "from KDB_INTERNAL",                     "");
        yield return new(302, "join kdb_lookup on x = y",              "");
        yield return new(303, "left join kdb_lookup on x = y",         "");
        yield return new(304, "from a, kdb_internal",                  "");
        yield return new(305, "insert into kdb_sink",                  "");
        yield return new(306, "update kdb_target set x = 1",           "");
        yield return new(307, "from \"kdb_internal\"",                 "");
        yield return new(308, "from main.kdb_internal",                "");
        yield return new(309, "with cte as (select * from kdb_internal) select * from cte", "");
        yield return new(310, "from (select * from kdb_internal) as t", "");
        yield return new(311, "from alias: kdb_internal",              "");

        // --- Nesting depth ---
        yield return new(350, new string('(', 33) + "select 1" + new string(')', 33), "");
    }

    #endregion

    #region Passthrough cases (empty options — no replacements configured)

    public static IEnumerable<NormalizerTestCase> PassthroughCases() {
        yield return new(400, "select 1",              "select 1");
        yield return new(401, "select a, b from t where x = 1",
                               "select a, b from t where x = 1");
        yield return new(402, "",                       "");
        yield return new(403, "select 'hello world'",   "select 'hello world'");
        yield return new(404, "from kdb_internal",      "from kdb_internal");
    }

    #endregion
}

#endif
