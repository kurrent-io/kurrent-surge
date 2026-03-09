using System.Collections.Immutable;
using System.Text;
using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

#if NET9_0_OR_GREATER

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class Utf8TranscodingNormalizerTests {
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

    static readonly QueryExpressionNormalizer       CharNormalizer = new(Options);
    static readonly Utf8TranscodingNormalizer Utf8Normalizer = new(CharNormalizer);

    #region Parity with char normalizer

    [Test]
    [Arguments("SELECT schema FROM records")]
    [Arguments("SELECT schema, timestamp FROM records WHERE data = 'test'")]
    [Arguments("SELECT schema FROM records AS r WHERE r.schema > 1")]
    [Arguments("SELECT schema FROM records LEFT JOIN events ON records.schema = events.schema")]
    [Arguments("WITH cte AS (SELECT schema FROM records) SELECT schema FROM cte")]
    [Arguments("SELECT schema::int FROM records")]
    [Arguments("SELECT \"schema\" FROM \"records\"")]
    [Arguments("SELECT schema FROM records -- comment\nWHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records /* block comment */ WHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records WHERE data = E'test\\n'")]
    [Arguments("SELECT schema FROM records WHERE data = $$dollar quoted$$")]
    [Arguments("SELECT schema FROM records WHERE data = $tag$dollar quoted$tag$")]
    [Arguments("SELECT 100_000, schema FROM records")]
    [Arguments("SELECT $1, schema FROM records")]
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
    public async Task rejects_forbidden_table_prefix() {
        var result = NormalizeUtf8(Utf8Normalizer, "SELECT schema FROM kdb_internal");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    #endregion

    #region Helpers

    static (bool IsValid, string? Value, string? Error) NormalizeChar(
        QueryExpressionNormalizer normalizer,
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
            var result = normalizer.Normalize(utf8Input, ref writer);

            return (
                result.IsValid,
                result.IsValid ? Encoding.UTF8.GetString(writer.WrittenSpan) : null,
                result.Error
            );
        }
        finally {
            writer.Dispose();
        }
    }

    #endregion
}

#endif
