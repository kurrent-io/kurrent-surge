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
}
