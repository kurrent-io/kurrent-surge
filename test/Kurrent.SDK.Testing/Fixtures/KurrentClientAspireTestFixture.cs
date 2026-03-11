#if NET10_0_OR_GREATER

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Kurrent.Client.Streams;
using Kurrent.Testing;
using Kurrent.Testing.Bogus;
using Kurrent.Testing.TUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Interfaces;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Client.Tests.Fixtures;

public sealed class KurrentClientAspireTestFixture : IAsyncInitializer, IAsyncDisposable {
    DistributedApplication? _app;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public ILogger Logger => TestContext.CurrrentLogger;

    public ILoggerFactory LoggerFactory => TestContext.CurrentLoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync() {
        var builder =  DistributedApplicationTestingBuilder.Create([], (options, settings) => {

        });

        _app = await builder.BuildAsync();

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

        await _app.StartAsync();

        // Wait for health only when Aspire manages the container.
        // When using an existing instance via connection string, the resource
        // is a reference with no health endpoint on the Aspire side.
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await notifications.WaitForResourceHealthyAsync("kurrentdb", cts.Token);
        } catch (InvalidOperationException) {
            // Resource is a connection string reference; no health check needed.
        } catch (OperationCanceledException ex) {
            throw new TimeoutException("KurrentDB did not become healthy within 2 minutes.", ex);
        }

        var baseConnectionString = await _app.GetConnectionStringAsync("kurrentdb")
            ?? throw new InvalidOperationException("Failed to get KurrentDB connection string.");

        // Aspire's connection string lacks credentials; inject default admin credentials.
        var uri = new Uri(baseConnectionString);
        ConnectionString = $"{uri.Scheme}://admin:changeit@{uri.Host}:{uri.Port}{uri.PathAndQuery}";

        Client = KurrentClientOptions.Parse(ConnectionString).GetBuilder()
            .WithLoggerFactory(new TestkitLoggerFactory())
            .ConfigureSchema(schema => schema
                .WithValidation(false)
                .ConfigureMessages(msg => msg.Map<StreamMetadata>("$metadata")))
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience)
            .CreateClient();
    }

    public async ValueTask DisposeAsync() {
        if (Client is not null)
            await Client.DisposeAsync();

        if (_app is not null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

#endif
