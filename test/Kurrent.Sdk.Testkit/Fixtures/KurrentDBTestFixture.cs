using Aspire.Hosting.ApplicationModel;
using Bogus;
using Kurrent.Client;
using Kurrent.Sdk.Testkit.Hosting;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Aspire;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Kurrent.Sdk.Testkit.Fixtures;

public class KurrentDBTestFixture : AspireFixture<Projects.Kurrent_Sdk_Testkit_AppHost> {

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    public ILogger        Logger        => TestkitExecutionContext.Logger;
    public ILoggerFactory LoggerFactory => TestkitExecutionContext.LoggerFactory;

    public FakeTimeProvider Time { get; } = new();

    public KurrentClient Client                    { get; private set; } = null!;
    public string        ConnectionString          { get; private set; } = string.Empty;
    public string        AnonymousConnectionString { get; private set; } = string.Empty;
    public string?       CertsDirectory            { get; private set; }

    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(2);

    protected override ResourceWaitBehavior WaitBehavior => ResourceWaitBehavior.Named;

    protected override IEnumerable<string> ResourcesToWaitFor() =>
        App.Services
            .GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<KurrentDBResource>()
            .Select(resource => resource.Name);

    public override async Task InitializeAsync() {
        await base.InitializeAsync();

        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = model.Resources
            .OfType<KurrentDBResource>()
            .First(resource => resource.Name == KurrentDBAppHostArgs.DefaultResourceName);

        ConnectionString = await resource.AuthenticatedConnectionStringExpression.GetValueAsync(CancellationToken.None)
                        ?? throw new InvalidOperationException("Authenticated connection string was null.");

        AnonymousConnectionString = await resource.ConnectionStringExpression.GetValueAsync(CancellationToken.None)
                        ?? throw new InvalidOperationException("Anonymous connection string was null.");

        CertsDirectory = resource.CertsHostPath;

        Client = GetNewClient();
    }

    public KurrentClient GetNewClient() =>
        KurrentClient.Create(ConnectionString, opts => {
            opts.LoggerFactory      = LoggerFactory;
            opts.Schema.PreValidate = false;
            opts.Resilience         = KurrentClientResilienceOptions.NoResilience;
        });

    public override async ValueTask DisposeAsync() {
        if (Client is { } client)
            await client.DisposeAsync();

        await base.DisposeAsync();

        if (CertsDirectory is { } dir)
            Directory.Delete(dir, recursive: true);
    }
}

public sealed class KurrentDBSecureTestFixture : KurrentDBTestFixture {
    static readonly string[] DefaultArgs = [KurrentDBAppHostArgs.Tls(true)];

    protected override string[] Args => DefaultArgs;
}

/// <summary>
/// Three-node cluster with TLS enabled. KurrentDB clustering requires TLS for
/// inter-node gossip, so this fixture is always secure.
/// </summary>
public sealed class KurrentDBClusterTestFixture : KurrentDBTestFixture {
    static readonly string[] DefaultArgs = [KurrentDBAppHostArgs.Nodes(3), KurrentDBAppHostArgs.Tls(true)];

    protected override string[] Args => DefaultArgs;
}
