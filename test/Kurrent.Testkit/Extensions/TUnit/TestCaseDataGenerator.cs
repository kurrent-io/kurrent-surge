namespace Kurrent.Testkit.TUnit;

public record TestCase<T>(T Data, string? DisplayName = null, string? Skip = null, string[]? Categories = null)
    : TestDataRow<T>(Data, DisplayName, Skip, Categories);

[PublicAPI]
public abstract class TestCaseDataGenerator<T> : DataSourceGeneratorAttribute<TestCase<T>> {
    protected override IEnumerable<Func<TestCase<T>>> GenerateDataSources(DataGeneratorMetadata dataGeneratorMetadata) =>
        Data().Select(x => new Func<TestCase<T>>(() => x));

    protected abstract IEnumerable<TestCase<T>> Data();
}