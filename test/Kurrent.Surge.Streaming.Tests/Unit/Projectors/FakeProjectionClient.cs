namespace Kurrent.Surge.Streaming.Tests.Projections;

sealed class FakeProjectionClient : IAsyncDisposable {
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
