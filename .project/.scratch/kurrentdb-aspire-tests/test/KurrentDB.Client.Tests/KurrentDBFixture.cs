using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Client.Tests;

public sealed class KurrentDBFixture : IAsyncLifetime {
    DistributedApplication _app = default!;

    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync() {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.KurrentDB_TestHost>();

        _app = await builder.BuildAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        await _app.StartAsync();

        // only wait for healthy if we spun up a container.
        // when using an existing instance via connection string, the resource
        // is just a reference — there's nothing to health-check on the Aspire side.
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            await notifications.WaitForResourceHealthyAsync("kurrentdb", cts.Token);

            var endpoint = _app.GetEndpoint("kurrentdb", "http");
            Console.WriteLine($"KurrentDB UI: {endpoint}");
        } catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) {
            // InvalidOperationException: resource is a connection string reference, no health check
            // OperationCanceledException: timed out waiting for healthy
            if (ex is OperationCanceledException)
                throw new TimeoutException("KurrentDB did not become healthy within 2 minutes.", ex);
        }

        ConnectionString = await _app.GetConnectionStringAsync("kurrentdb")
            ?? throw new InvalidOperationException("Failed to get KurrentDB connection string.");

        Console.WriteLine($"KurrentDB connection: {ConnectionString}");
    }

    public async Task DisposeAsync() {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public KurrentDBClient CreateClient() =>
        new(KurrentDBClientSettings.Create(ConnectionString));
}

[CollectionDefinition(Name)]
public class KurrentDBCollection : ICollectionFixture<KurrentDBFixture> {
    public const string Name = "KurrentDB";
}
