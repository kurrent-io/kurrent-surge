using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class NewQueryExpressionNormalizerTests {
    static readonly LegacyQueryExpressionNormalizer LegacyNormalizer = new(NormalizerTestCases.Options);
    static readonly QueryExpressionNormalizer       NewNormalizer    = new(NormalizerTestCases.Options);

    #region Success cases (parity with legacy)

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.SuccessCases))]
    public async Task normalizes_correctly(NormalizerTestCase c) {
        var legacyResult = NormalizeLegacy(LegacyNormalizer, c.Query);
        var newResult    = NormalizeNew(NewNormalizer, c.Query);

        await Assert.That(newResult.IsValid).IsTrue();
        await Assert.That(newResult.Value).IsEqualTo(c.Expected);
        await Assert.That(newResult.Value).IsEqualTo(legacyResult.Value);
    }

    #endregion

    #region Error cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.ErrorCases))]
    public async Task rejects_invalid_query(NormalizerTestCase c) {
        var legacyResult = NormalizeLegacy(LegacyNormalizer, c.Query);
        var newResult    = NormalizeNew(NewNormalizer, c.Query);

        await Assert.That(newResult.IsValid).IsFalse();
        await Assert.That(newResult.IsValid).IsEqualTo(legacyResult.IsValid);
    }

    #endregion

    #region Passthrough cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.PassthroughCases))]
    public async Task passes_through_with_empty_options(NormalizerTestCase c) {
        var noReplacements = new QueryExpressionNormalizer(new NormalizerOptions());
        var result         = NormalizeNew(noReplacements, c.Query);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(c.Expected);
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
