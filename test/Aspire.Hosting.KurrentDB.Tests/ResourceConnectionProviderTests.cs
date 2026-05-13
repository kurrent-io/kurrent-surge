using Aspire.Hosting.Eventing;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.KurrentDB.Tests;

[Category("Hosting")]
public class ResourceConnectionProviderTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask resolves_connection_strings_for_all_resources() {
        // Arrange
        var resourceName1 = Faker.Lorem.Word() + "-1";
        var resourceName2 = Faker.Lorem.Word() + "-2";
        var connString1   = $"host={Faker.Internet.Ip()};port={Faker.Internet.Port()}";
        var connString2   = $"host={Faker.Internet.Ip()};port={Faker.Internet.Port()}";

        await using var fixture = await CreateApp(
            new FakeResource(resourceName1, connString1),
            new FakeResource(resourceName2, connString2));

        // Act
        var result1 = await fixture.Subscriber.GetConnectionStringAsync(resourceName1).ConfigureAwait(false);
        var result2 = await fixture.Subscriber.GetConnectionStringAsync(resourceName2).ConfigureAwait(false);

        // Assert
        await Assert.That(result1).IsEqualTo(connString1);
        await Assert.That(result2).IsEqualTo(connString2);
    }

    [Test]
    public async ValueTask completes_immediately_when_no_resources_exist() {
        // Arrange
        await using var fixture = await CreateApp();

        // Act & Assert
        var lookup = async () => await fixture.Subscriber.GetConnectionStringAsync("nonexistent").ConfigureAwait(false);
        await Assert.That(lookup).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async ValueTask throws_for_unknown_resource_name() {
        // Arrange
        var resourceName = Faker.Lorem.Word();
        var connString   = $"host={Faker.Internet.Ip()}";

        await using var fixture = await CreateApp(new FakeResource(resourceName, connString));

        // Act & Assert
        var lookup = async () => await fixture.Subscriber.GetConnectionStringAsync("does-not-exist").ConfigureAwait(false);
        await Assert.That(lookup).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Timeout(10_000)]
    public async ValueTask respects_cancellation_when_resource_never_resolves(CancellationToken ct) {
        // Arrange
        var builder = DistributedApplicationTestingBuilder.Create([]);
        RegisterSubscriber(builder);
        builder.AddResource(new FakeResource("stuck", "unreachable"));

        await using var app = await builder.BuildAsync(ct).ConfigureAwait(false);
        await app.StartAsync(ct).ConfigureAwait(false);

        var subscriber = app.Services.GetRequiredService<ResourceConnectionProvider>();

        // Act & Assert — event is never published, so the subscriber blocks until timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var lookup = async () => await subscriber.GetConnectionStringAsync("stuck", cts.Token).ConfigureAwait(false);
        await Assert.That(lookup).ThrowsException();
    }

    [Test]
    public async ValueTask resolves_single_resource() {
        // Arrange
        var resourceName = Faker.Lorem.Word();
        var connString   = $"host={Faker.Internet.Ip()};port={Faker.Internet.Port()}";

        await using var fixture = await CreateApp(new FakeResource(resourceName, connString));

        // Act
        var result = await fixture.Subscriber.GetConnectionStringAsync(resourceName).ConfigureAwait(false);

        // Assert
        await Assert.That(result).IsEqualTo(connString);
    }

    #region ->> Helpers <<-

    static void RegisterSubscriber(IDistributedApplicationTestingBuilder builder) =>
        builder.Services.AddResourceConnectionProvider();

    static async Task<AppFixture> CreateApp(params FakeResource[] resources) {
        var builder = DistributedApplicationTestingBuilder.Create([]);
       
        RegisterSubscriber(builder);

        foreach (var resource in resources)
            builder.AddResource(resource);

        var app = await builder.BuildAsync().ConfigureAwait(false);
        
        await app.StartAsync().ConfigureAwait(false);

        var eventing   = app.Services.GetRequiredService<IDistributedApplicationEventing>();
        var subscriber = app.Services.GetRequiredService<ResourceConnectionProvider>();

        foreach (var resource in resources)
            await eventing.PublishAsync(new ConnectionStringAvailableEvent(resource, app.Services)).ConfigureAwait(false);

        return new(app, subscriber);
    }

    #endregion

    #region ->> Test Fixtures <<-

    sealed class AppFixture(DistributedApplication app, ResourceConnectionProvider subscriber) : IAsyncDisposable {
        public ResourceConnectionProvider Subscriber => subscriber;

        public async ValueTask DisposeAsync() {
            await app.StopAsync().ConfigureAwait(false);
            await ((IAsyncDisposable)app).DisposeAsync().ConfigureAwait(false);
        }
    }

    sealed class FakeResource(string name, string connectionString) : ContainerResource(name), IResourceWithConnectionString {
        public ReferenceExpression ConnectionStringExpression =>
            ReferenceExpression.Create($"{connectionString}");

        public IEnumerable<KeyValuePair<string, ReferenceExpression>> GetConnectionProperties() {
            yield return new("ConnectionString", ConnectionStringExpression);
        }
    }

    #endregion
}
