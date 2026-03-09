using System.Collections.Immutable;
using System.Text;
using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

#if NET10_0_OR_GREATER

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class Utf8QueryExpressionNormalizerTests {
    static readonly NormalizerOptions Options = new() {
        ColumnReplacements = new Dictionary<string, string> {
            ["schema"]     = "event_type",
            ["timestamp"]  = "created",
            ["data"]       = "payload",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        TableReplacements = new Dictionary<string, string> {
            ["records"] = "kdb_log",
            ["events"]  = "kdb_events",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        ForbiddenTablePrefix = "kdb_",
    };

    static readonly LegacyQueryExpressionNormalizer CharNormalizer = new(Options);
    static readonly Utf8QueryExpressionNormalizer   Utf8Normalizer = new(Options);

    #region Parity with char normalizer

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
    // Multi-byte UTF-8 in string literals
    [Arguments("SELECT schema FROM records WHERE data = '\u00e9\u00e8\u00ea'")]
    [Arguments("SELECT schema FROM records WHERE data = '\u4e16\u754c'")]
    [Arguments("SELECT schema FROM records WHERE data = '\ud83d\ude80\ud83c\udf1f'")]
    // Multi-byte UTF-8 in comments
    [Arguments("SELECT schema FROM records -- \u00e9\u00e8\u00ea comment\nWHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records /* \u4e16\u754c block comment */ WHERE timestamp > 0")]
    // Multi-byte UTF-8 in dollar-quoted strings
    [Arguments("SELECT schema FROM records WHERE data = $$\u00e9\u00e8\u00ea$$")]
    [Arguments("SELECT schema FROM records WHERE data = $tag$\u4e16\u754c$tag$")]
    // Multi-byte UTF-8 in E-strings
    [Arguments("SELECT schema FROM records WHERE data = E'\u00e9\\n\u00e8'")]
    public async Task produces_same_output_as_char_normalizer(string input) {
        var charResult = NormalizeChar(CharNormalizer, input);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, input);

        await Assert.That(utf8Result.IsValid).IsEqualTo(charResult.IsValid);
        await Assert.That(utf8Result.Value).IsEqualTo(charResult.Value);
        await Assert.That(utf8Result.Error).IsEqualTo(charResult.Error);
    }

    #endregion

    #region Forbidden prefix

    [Test]
    [Arguments("SELECT schema FROM kdb_internal")]
    [Arguments("SELECT schema FROM \"kdb_internal\"")]
    public async Task rejects_forbidden_table_prefix(string input) {
        var charResult = NormalizeChar(CharNormalizer, input);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, input);

        await Assert.That(utf8Result.IsValid).IsFalse();
        await Assert.That(utf8Result.IsValid).IsEqualTo(charResult.IsValid);
    }

    #endregion

    #region Nesting depth

    [Test]
    public async Task rejects_excessive_nesting() {
        var input = new string('(', 33) + "SELECT 1" + new string(')', 33);

        var charResult = NormalizeChar(CharNormalizer, input);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, input);

        await Assert.That(utf8Result.IsValid).IsFalse();
        await Assert.That(utf8Result.IsValid).IsEqualTo(charResult.IsValid);
    }

    #endregion

    #region Passthrough (no replacements)

    [Test]
    [Arguments("SELECT 1")]
    [Arguments("SELECT 'hello world'")]
    [Arguments("")]
    public async Task passes_through_when_no_replacements_match(string input) {
        var noReplacements = new Utf8QueryExpressionNormalizer(new NormalizerOptions());
        var result         = NormalizeUtf8(noReplacements, input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(input);
    }

    #endregion

    #region Helpers

    static (bool IsValid, string? Value, string? Error) NormalizeChar(
        LegacyQueryExpressionNormalizer normalizer,
        string input
    ) {
        var buffer = new char[input.Length * 2 + 1];
        var result = normalizer.Normalize(input.AsSpan(), buffer.AsSpan());

        return (result.IsValid, result.IsValid ? result.Value.ToString() : null, result.Error);
    }

    static (bool IsValid, string? Value, string? Error) NormalizeUtf8(
        IUtf8QueryExpressionNormalizer normalizer,
        string input
    ) {
        var utf8Input = Encoding.UTF8.GetBytes(input);
        var writer    = new BufferWriterSlim<byte>(stackalloc byte[256]);

        try {
            normalizer.Normalize(utf8Input, ref writer);
            return (true, Encoding.UTF8.GetString(writer.WrittenSpan), null);
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
