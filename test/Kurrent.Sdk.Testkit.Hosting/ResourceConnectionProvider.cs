using System.Collections.Concurrent;

namespace Aspire.Hosting.ApplicationModel;

public sealed class ResourceConnectionProvider<TResource>
    where TResource : IResourceWithConnectionString {
    readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _sources = new();

    public void Register(IDistributedApplicationBuilder app, TResource resource) {
        var source = GetSource(resource.Name);
        app.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (evt, ct) => {
            var cs = await resource.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);
            if (cs is not null)
                source.TrySetResult(cs);
        });
    }

    public string GetConnectionString(string resourceName) =>
        GetSource(resourceName).Task is { IsCompleted: true } t
            ? t.Result
            : throw new InvalidOperationException($"Connection string for '{resourceName}' is not yet available.");

    public Task<string> GetConnectionStringAsync(string resourceName, CancellationToken ct = default) =>
        GetSource(resourceName).Task.WaitAsync(ct);

    public Task<string> GetConnectionStringAsync(string resourceName, TimeSpan timeout) =>
        GetSource(resourceName).Task.WaitAsync(timeout);

    TaskCompletionSource<string> GetSource(string name) =>
        _sources.GetOrAdd(name, _ => new());
}
