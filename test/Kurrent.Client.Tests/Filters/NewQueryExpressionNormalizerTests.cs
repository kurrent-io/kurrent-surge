using System.Collections.Immutable;
using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

#if NET9_0_OR_GREATER

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class NewQueryExpressionNormalizerTests {
    static readonly NormalizerOptions Options = new() {
        ColumnReplacements = new Dictionary<string, string> {
            ["schema"]    = "event_type",
            ["timestamp"] = "created",
            ["data"]      = "payload",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        TableReplacements = new Dictionary<string, string> {
            ["records"] = "kdb_log",
            ["events"]  = "kdb_events",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        ForbiddenTablePrefix = "kdb_"
    };

    static readonly LegacyQueryExpressionNormalizer LegacyNormalizer = new(Options);
    static readonly QueryExpressionNormalizer       NewNormalizer    = new(Options);

    #region Parity with legacy normalizer

    [Test]
    [Arguments("SELECT schema FROM records")]
    [Arguments("SELECT schema, timestamp FROM records WHERE data = 'test'")]
    [Arguments("SELECT schema FROM records AS r WHERE r.schema > 1")]
    [Arguments("SELECT schema FROM records LEFT JOIN events ON records.schema = events.schema")]
    [Arguments("WITH cte AS (SELECT schema FROM records) SELECT schema FROM cte")]
    [Arguments("WITH RECURSIVE cte AS (SELECT schema FROM records) SELECT schema FROM cte")]
    [Arguments("SELECT schema::int FROM records")]
    [Arguments("SELECT \"schema\" FROM \"records\"")]
    [Arguments("SELECT schema FROM records -- comment\nWHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records /* block comment */ WHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records WHERE data = E'test\\n'")]
    [Arguments("SELECT schema FROM records WHERE data = $$dollar quoted$$")]
    [Arguments("SELECT schema FROM records WHERE data = $tag$dollar quoted$tag$")]
    [Arguments("SELECT 100_000, schema FROM records")]
    [Arguments("SELECT $1, schema FROM records")]
    [Arguments("SELECT schema FROM records WHERE data[1] = 'test'")]
    [Arguments("SELECT alias: schema FROM records")]
    [Arguments("SELECT LAMBDA x, y: x + y FROM records")]
    [Arguments("SELECT schema FROM public.records")]
    [Arguments("SELECT schema FROM records, events")]
    [Arguments("WITH a AS (SELECT 1), b AS (SELECT 2) SELECT schema FROM a")]
    [Arguments("SELECT schema FROM (SELECT schema FROM records) AS sub")]
    public async Task produces_same_output_as_legacy_normalizer(string input) {
        var legacyResult = NormalizeLegacy(LegacyNormalizer, input);
        var newResult    = NormalizeNew(NewNormalizer, input);

        await Assert.That(newResult.IsValid).IsEqualTo(legacyResult.IsValid);
        await Assert.That(newResult.Value).IsEqualTo(legacyResult.Value);
        await Assert.That(newResult.Error).IsEqualTo(legacyResult.Error);
    }

    #endregion

    #region Forbidden prefix

    [Test]
    [Arguments("SELECT schema FROM kdb_internal")]
    [Arguments("SELECT schema FROM \"kdb_internal\"")]
    [Arguments("SELECT schema FROM public.kdb_internal")]
    public async Task rejects_forbidden_table_prefix(string input) {
        var legacyResult = NormalizeLegacy(LegacyNormalizer, input);
        var newResult    = NormalizeNew(NewNormalizer, input);

        await Assert.That(newResult.IsValid).IsFalse();
        await Assert.That(newResult.IsValid).IsEqualTo(legacyResult.IsValid);
    }

    #endregion

    #region Nesting depth

    [Test]
    public async Task rejects_excessive_nesting() {
        var input = new string('(', 33) + "SELECT 1" + new string(')', 33);

        var legacyResult = NormalizeLegacy(LegacyNormalizer, input);
        var newResult    = NormalizeNew(NewNormalizer, input);

        await Assert.That(newResult.IsValid).IsFalse();
        await Assert.That(newResult.IsValid).IsEqualTo(legacyResult.IsValid);
    }

    #endregion

    #region Passthrough (no replacements)

    [Test]
    [Arguments("SELECT 1")]
    [Arguments("SELECT 'hello world'")]
    [Arguments("")]
    public async Task passes_through_when_no_replacements_match(string input) {
        var noReplacements = new QueryExpressionNormalizer(new NormalizerOptions());
        var result         = NormalizeNew(noReplacements, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(input);
    }

    #endregion

    #region Helpers

    static (bool IsValid, string? Value, string? Error) NormalizeLegacy(
        LegacyQueryExpressionNormalizer normalizer,
        string input
    ) {
        var buffer = new char[input.Length * 2 + 1];
        var result = normalizer.Normalize(input.AsSpan(), buffer.AsSpan());

        return (result.IsValid, result.IsValid ? result.Value.ToString() : null, result.Error);
    }

    static (bool IsValid, string? Value, string? Error) NormalizeNew(
        QueryExpressionNormalizer normalizer,
        string input
    ) {
        var writer = new BufferWriterSlim<char>(stackalloc char[256]);

        try {
            normalizer.Normalize(input.AsSpan(), ref writer);
            return (true, new string(writer.WrittenSpan), null);
        }
        catch (QueryNormalizationException ex) {
            return (false, null, ex.Message);
        }
        finally {
            writer.Dispose();
        }
    }

    #endregion
}

#endif
