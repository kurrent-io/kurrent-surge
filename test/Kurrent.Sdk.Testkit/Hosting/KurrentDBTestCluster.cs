using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Sdk.Testkit.Hosting;

/// <summary>
/// Thin harness over <see cref="DistributedApplicationTestingBuilder"/>. Callers pass a
/// configure callback that composes whatever KurrentDB topology they want; the harness
/// owns lifecycle (build, start, wait until healthy, extract connection strings, clean
/// up cert directories on dispose). Topology decisions belong to the caller.
/// </summary>
public sealed class KurrentDBTestCluster : IAsyncDisposable {
    readonly DistributedApplication _app;
    readonly IReadOnlyList<string>  _certsDirectories;

    KurrentDBTestCluster(
        DistributedApplication app,
        string                 authenticated,
        string                 anonymous,
        IReadOnlyList<string>  certsDirectories
    ) {
        _app                          = app;
        _certsDirectories             = certsDirectories;
        AuthenticatedConnectionString = authenticated;
        AnonymousConnectionString     = anonymous;
    }

    public string  AuthenticatedConnectionString { get; }
    public string  AnonymousConnectionString     { get; }
    public string? CertsDirectory                => _certsDirectories.FirstOrDefault();

    public static async Task<KurrentDBTestCluster> StartAsync(
        Action<IResourceBuilder<KurrentDBResource>>? configure = null,
        CancellationToken ct = default
    ) {
        var builder = DistributedApplicationTestingBuilder.Create();
        var seed    = builder.AddKurrentDB("kurrentdb");
        configure?.Invoke(seed);

        var nodes   = builder.Resources.OfType<KurrentDBResource>().ToArray();
        var primary = nodes.Single(n => n.IsPrimary);

        var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);

        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        foreach (var node in nodes)
            await WaitUntilHealthy(notifications, node.Name, ct);

        var anonymous     = await primary.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                         ?? throw new InvalidOperationException($"Connection string for primary '{primary.Name}' was null.");
        var authenticated = await primary.AuthenticatedConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                         ?? throw new InvalidOperationException($"Authenticated connection string for primary '{primary.Name}' was null.");

        var certsDirectories = nodes
            .Select(n => n.CertsHostPath)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new KurrentDBTestCluster(app, authenticated, anonymous, certsDirectories);
    }

    public async ValueTask DisposeAsync() {
        await _app.StopAsync();
        await _app.DisposeAsync();

        foreach (var path in _certsDirectories)
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
    }

    static async Task WaitUntilHealthy(ResourceNotificationService notifications, string resourceName, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(2));

        try {
            await notifications.WaitForResourceHealthyAsync(resourceName, cts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            throw new TimeoutException(
                $"KurrentDB resource '{resourceName}' did not become healthy within 2 minutes.", ex);
        }
    }
}
