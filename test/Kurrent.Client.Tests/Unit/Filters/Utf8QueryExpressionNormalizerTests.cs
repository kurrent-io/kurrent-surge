using System.Text;
using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class Utf8QueryExpressionNormalizerTests {
    static readonly LegacyQueryExpressionNormalizer CharNormalizer = new(NormalizerTestCases.Options);
    static readonly Utf8QueryExpressionNormalizer   Utf8Normalizer = new(NormalizerTestCases.Options);

    #region Success cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.SuccessCases))]
    public async Task normalizes_correctly(NormalizerTestCase c) {
        var charResult = NormalizeChar(CharNormalizer, c.Query);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, c.Query);

        await Assert.That(utf8Result.IsValid).IsTrue();
        await Assert.That(utf8Result.Value).IsEqualTo(c.Expected);
        await Assert.That(utf8Result.Value).IsEqualTo(charResult.Value);
    }

    #endregion

    #region Error cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.ErrorCases))]
    public async Task rejects_invalid_query(NormalizerTestCase c) {
        var charResult = NormalizeChar(CharNormalizer, c.Query);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, c.Query);

        await Assert.That(utf8Result.IsValid).IsFalse();
        await Assert.That(utf8Result.IsValid).IsEqualTo(charResult.IsValid);
    }

    #endregion

    #region Passthrough cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.PassthroughCases))]
    public async Task passes_through_with_empty_options(NormalizerTestCase c) {
        var noReplacements = new Utf8QueryExpressionNormalizer(new NormalizerOptions());
        var result         = NormalizeUtf8(noReplacements, c.Query);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(c.Expected);
    }

    #endregion

    #region Multi-byte UTF-8

    [Test]
    [Arguments("SELECT schema FROM records WHERE data = 'éèê'")]
    [Arguments("SELECT schema FROM records WHERE data = '世界'")]
    [Arguments("SELECT schema FROM records WHERE data = '🚀🌟'")]
    [Arguments("SELECT schema FROM records -- éèê comment\nWHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records /* 世界 block comment */ WHERE timestamp > 0")]
    [Arguments("SELECT schema FROM records WHERE data = $$éèê$$")]
    [Arguments("SELECT schema FROM records WHERE data = $tag$世界$tag$")]
    [Arguments("SELECT schema FROM records WHERE data = E'é\\nè'")]
    public async Task handles_multi_byte_utf8(string input) {
        var charResult = NormalizeChar(CharNormalizer, input);
        var utf8Result = NormalizeUtf8(Utf8Normalizer, input);

        await Assert.That(utf8Result.IsValid).IsEqualTo(charResult.IsValid);
        await Assert.That(utf8Result.Value).IsEqualTo(charResult.Value);
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
