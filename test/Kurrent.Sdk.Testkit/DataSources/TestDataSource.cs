namespace Testing.ArcadeRacer;

public interface ITestDataSource {
    string Name { get; }

    ValueTask<TestStream> GenerateSet(int numberOfEvents, CancellationToken cancellationToken);

    ValueTask<TestStream> GenerateSet(CancellationToken cancellationToken) =>
        GenerateSet(TestDataSource.UnspecifiedCount, cancellationToken);

    async ValueTask<TestStream[]> GenerateManySets(int numberOfSets, CancellationToken cancellationToken) =>
        await Task.WhenAll(
            Enumerable.Range(0, numberOfSets)
                .Select(_ => GenerateSet(TestDataSource.UnspecifiedCount, cancellationToken).AsTask()));

    ValueTask<TestStream[]> GenerateManySets(CancellationToken cancellationToken) =>
        GenerateManySets(TestDataSource.UnspecifiedCount, cancellationToken);

    ValueTask<TestStream> GenerateRecord(CancellationToken cancellationToken) =>
        GenerateSet(1, cancellationToken);
}

static class TestDataSource {
    public const int UnspecifiedCount = -1;

    public static bool IsUnspecifiedCount(int count) => count <= UnspecifiedCount;
}
