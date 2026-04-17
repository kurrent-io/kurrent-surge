using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

[Category("Filters")]
[Category("QueryExpressionNormalizer")]
public class LegacyQueryExpressionNormalizerTests {
    static readonly LegacyQueryExpressionNormalizer Normalizer = new(NormalizerTestCases.Options);

    #region Success cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.SuccessCases))]
    public async Task normalizes_correctly(NormalizerTestCase c) {
        var result = Normalize(Normalizer, c.Query);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(c.Expected);
    }

    #endregion

    #region Error cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.ErrorCases))]
    public async Task rejects_invalid_query(NormalizerTestCase c) {
        var result = Normalize(Normalizer, c.Query);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsNotNull();
    }

    #endregion

    #region Passthrough cases

    [Test]
    [MethodDataSource(typeof(NormalizerTestCases), nameof(NormalizerTestCases.PassthroughCases))]
    public async Task passes_through_with_empty_options(NormalizerTestCase c) {
        var empty  = new LegacyQueryExpressionNormalizer(new NormalizerOptions());
        var result = Normalize(empty, c.Query);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value).IsEqualTo(c.Expected);
    }

    #endregion

    #region Helpers

    static (bool IsValid, string? Value, string? Error) Normalize(
        LegacyQueryExpressionNormalizer normalizer,
        string input
    ) {
        var buffer = new char[input.Length * 2 + 1];
        var result = normalizer.Normalize(input.AsSpan(), buffer.AsSpan());
        return (result.IsValid, result.IsValid ? result.Value.ToString() : null, result.Error);
    }

    #endregion
}
