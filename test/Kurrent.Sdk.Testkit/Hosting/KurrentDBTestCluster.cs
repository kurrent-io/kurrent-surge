using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Sdk.Testkit.Hosting;

/// <summary>
/// Thin harness over <see cref="DistributedApplicationTestingBuilder"/>. Spins up a
/// KurrentDB topology (single node by default; cluster + TLS via <see cref="KurrentDBTopology"/>)
/// and optionally a user-side <see cref="IHost"/> with services bound to the cluster's
/// connection string under <c>ConnectionStrings:kurrentdb</c>.
/// </summary>
public sealed class KurrentDBTestCluster : IAsyncDisposable {
    const string ResourceName = "kurrentdb";

    readonly DistributedApplication _app;
    readonly IHost?                 _userHost;

    KurrentDBTestCluster(
        DistributedApplication app,
        IHost? userHost,
        string authenticated,
        string anonymous,
        string? certsDirectory
    ) {
        _app                          = app;
        _userHost                     = userHost;
        AuthenticatedConnectionString = authenticated;
        AnonymousConnectionString     = anonymous;
        CertsDirectory                = certsDirectory;
    }

    public string  AuthenticatedConnectionString { get; }
    public string  AnonymousConnectionString     { get; }
    public string? CertsDirectory                { get; }

    /// <summary>
    /// Service provider for the user-side host built from <c>configureServices</c>. Throws if
    /// no <c>configureServices</c> callback was supplied to <see cref="StartAsync"/>.
    /// </summary>
    public IServiceProvider Services =>
        _userHost?.Services ?? throw new InvalidOperationException("No user services were configured. Pass a configureServices callback to StartAsync to enable Services.");

    public static async Task<KurrentDBTestCluster> StartAsync(
        KurrentDBTopology? topology = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken ct = default
    ) {
        var builder = DistributedApplicationTestingBuilder.Create();
        var entry   = builder.AddKurrentDB(ResourceName, topology).Resource;

        var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);

        await WaitUntilHealthy(app, builder.Resources.OfType<KurrentDBResource>(), ct);

        var anonymous = await entry.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"Connection string for '{entry.Name}' was null.");
        var authenticated = await entry.AuthenticatedConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"Authenticated connection string for '{entry.Name}' was null.");

        var userHost = configureServices is null
            ? null
            : await StartUserHost(authenticated, configureServices, ct);

        return new KurrentDBTestCluster(app, userHost, authenticated, anonymous, entry.CertsHostPath);
    }

    static async ValueTask<IHost> StartUserHost(
        string connectionString, Action<IServiceCollection> configure, CancellationToken ct
    ) {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration[$"ConnectionStrings:{ResourceName}"] = connectionString;
        configure(hostBuilder.Services);

        var host = hostBuilder.Build();
        await host.StartAsync(ct);
        return host;
    }

    public async ValueTask DisposeAsync() {
        if (_userHost is { }) {
            await _userHost.StopAsync();
            _userHost.Dispose();
        }

        await _app.StopAsync();
        await _app.DisposeAsync();

        if (CertsDirectory is { } && Directory.Exists(CertsDirectory))
            Directory.Delete(CertsDirectory, recursive: true);
    }

    static async Task WaitUntilHealthy(
        DistributedApplication app, IEnumerable<KurrentDBResource> resources, CancellationToken ct
    ) {
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(2));

        try {
            await Task.WhenAll(resources.Select(r =>
                notifications.WaitForResourceHealthyAsync(r.Name, cts.Token)));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            throw new TimeoutException("KurrentDB cluster did not become healthy within 2 minutes.", ex);
        }
    }
}
