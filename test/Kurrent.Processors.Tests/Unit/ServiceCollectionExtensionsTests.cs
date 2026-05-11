using Kurrent.Processors.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class ServiceCollectionExtensionsTests {

    #region ->> IsRegistered <<-

    [Test]
    public async ValueTask is_registered_returns_false_when_not_registered() {
        var services = new ServiceCollection();

        await Assert.That(services.IsRegistered<IFoo>()).IsFalse();
    }

    [Test]
    public async ValueTask is_registered_returns_true_when_registered() {
        var services = new ServiceCollection();
        services.AddSingleton<IFoo, Foo>();

        await Assert.That(services.IsRegistered<IFoo>()).IsTrue();
    }

    [Test]
    public async ValueTask is_registered_respects_service_key() {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFoo, Foo>("a");

        await Assert.That(services.IsRegistered<IFoo>("a")).IsTrue();
        await Assert.That(services.IsRegistered<IFoo>("b")).IsFalse();
        await Assert.That(services.IsRegistered<IFoo>()).IsFalse();
    }

    [Test]
    public async ValueTask is_registered_by_type_argument() {
        var services = new ServiceCollection();
        services.AddSingleton<Foo>();

        await Assert.That(services.IsRegistered(typeof(Foo))).IsTrue();
        await Assert.That(services.IsRegistered(typeof(IFoo))).IsFalse();
    }

    #endregion // IsRegistered

    #region ->> TryRegister — non-keyed <<-

    [Test]
    public async ValueTask try_register_self_adds_on_first_call() {
        var services = new ServiceCollection();

        var added = services.TryRegister<Foo>();

        await Assert.That(added).IsTrue();
        await Assert.That(services.IsRegistered<Foo>()).IsTrue();
    }

    [Test]
    public async ValueTask try_register_self_skips_on_second_call() {
        var services = new ServiceCollection();
        services.TryRegister<Foo>();

        var added = services.TryRegister<Foo>();

        await Assert.That(added).IsFalse();
        await Assert.That(services.Count(d => d.ServiceType == typeof(Foo))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask try_register_service_impl_adds_descriptor() {
        var services = new ServiceCollection();

        var added = services.TryRegister<IFoo, Foo>();

        await Assert.That(added).IsTrue();
        var descriptor = services.Single(d => d.ServiceType == typeof(IFoo));
        await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(Foo));
    }

    [Test]
    public async ValueTask try_register_factory_adds_descriptor() {
        var services = new ServiceCollection();

        var added = services.TryRegister<IFoo>(_ => new Foo());

        await Assert.That(added).IsTrue();
        var descriptor = services.Single(d => d.ServiceType == typeof(IFoo));
        await Assert.That(descriptor.ImplementationFactory).IsNotNull();
    }

    [Test]
    public async ValueTask try_register_instance_adds_descriptor() {
        var services = new ServiceCollection();
        var instance = new Foo();

        var added = services.TryRegisterInstance<IFoo>(instance);

        await Assert.That(added).IsTrue();
        var descriptor = services.Single(d => d.ServiceType == typeof(IFoo));
        await Assert.That(descriptor.ImplementationInstance).IsEqualTo(instance);
    }

    [Test]
    [Arguments(ServiceLifetime.Singleton)]
    [Arguments(ServiceLifetime.Scoped)]
    [Arguments(ServiceLifetime.Transient)]
    public async ValueTask try_register_honors_lifetime(ServiceLifetime lifetime) {
        var services = new ServiceCollection();

        services.TryRegister<Foo>(lifetime);

        var descriptor = services.Single(d => d.ServiceType == typeof(Foo));
        await Assert.That(descriptor.Lifetime).IsEqualTo(lifetime);
    }

    [Test]
    public async ValueTask try_register_default_lifetime_is_singleton() {
        var services = new ServiceCollection();

        services.TryRegister<Foo>();

        var descriptor = services.Single(d => d.ServiceType == typeof(Foo));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
    }

    #endregion // TryRegister — non-keyed

    #region ->> TryRegisterKeyed <<-

    [Test]
    public async ValueTask try_register_keyed_adds_distinct_entry_per_key() {
        var services = new ServiceCollection();

        var a = services.TryRegisterKeyed<Foo>("a");
        var b = services.TryRegisterKeyed<Foo>("b");

        await Assert.That(a).IsTrue();
        await Assert.That(b).IsTrue();
        await Assert.That(services.Count(d => d.ServiceType == typeof(Foo))).IsEqualTo(2);
    }

    [Test]
    public async ValueTask try_register_keyed_skips_on_duplicate_key() {
        var services = new ServiceCollection();
        services.TryRegisterKeyed<Foo>("a");

        var added = services.TryRegisterKeyed<Foo>("a");

        await Assert.That(added).IsFalse();
        await Assert.That(services.Count(d => d.ServiceType == typeof(Foo))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask keyed_and_non_keyed_are_distinct() {
        var services = new ServiceCollection();

        var keyed    = services.TryRegisterKeyed<Foo>("a");
        var nonKeyed = services.TryRegister<Foo>();

        await Assert.That(keyed).IsTrue();
        await Assert.That(nonKeyed).IsTrue();
        await Assert.That(services.Count(d => d.ServiceType == typeof(Foo))).IsEqualTo(2);
    }

    [Test]
    public async ValueTask try_register_keyed_instance_adds_descriptor() {
        var services = new ServiceCollection();
        var instance = new Foo();

        var added = services.TryRegisterKeyedInstance<IFoo>("a", instance);

        await Assert.That(added).IsTrue();
        var descriptor = services.Single(d => d.ServiceType == typeof(IFoo));
        await Assert.That(descriptor.ServiceKey).IsEqualTo("a");
        await Assert.That(descriptor.KeyedImplementationInstance).IsEqualTo(instance);
    }

    #endregion // TryRegisterKeyed

    #region ->> TryRegisterActivated <<-

    [Test]
    public async ValueTask try_register_activated_adds_and_marks_for_activation() {
        var services = new ServiceCollection();

        var added = services.TryRegisterActivated<Foo>();

        await Assert.That(added).IsTrue();
        await Assert.That(services.IsRegistered<Foo>()).IsTrue();
        // AutoActivation registers AutoActivationHostedService as IHostedService
        await Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService))).IsTrue();
    }

    [Test]
    public async ValueTask try_register_activated_skips_on_duplicate() {
        var services = new ServiceCollection();
        services.TryRegisterActivated<Foo>();

        var added = services.TryRegisterActivated<Foo>();

        await Assert.That(added).IsFalse();
    }

    [Test]
    public async ValueTask try_register_activated_resolves_at_host_start() {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryRegisterActivated<Foo>();

        var provider      = services.BuildServiceProvider();
        var host          = provider.GetServices<IHostedService>().ToArray();
        var countBefore   = Foo.ConstructedCount;

        // Act
        await Assert.That(host.Length).IsGreaterThan(0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var h in host)
            await h.StartAsync(cts.Token);

        // Assert
        await Assert.That(Foo.ConstructedCount - countBefore).IsEqualTo(1);
    }

    [Test]
    public async ValueTask try_register_keyed_activated_adds_distinct_entries() {
        var services = new ServiceCollection();

        var a = services.TryRegisterKeyedActivated<Foo>("a");
        var b = services.TryRegisterKeyedActivated<Foo>("b");

        await Assert.That(a).IsTrue();
        await Assert.That(b).IsTrue();
    }

    #endregion // TryRegisterActivated

    #region ->> TryRegisterHostedService <<-

    [Test]
    public async ValueTask try_register_hosted_service_adds_on_first_call() {
        var services = new ServiceCollection();

        var added = services.TryRegisterHostedService<TestHostedService>();

        await Assert.That(added).IsTrue();
        await Assert.That(services.IsRegistered<TestHostedService>()).IsTrue();
        await Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService))).IsTrue();
    }

    [Test]
    public async ValueTask try_register_hosted_service_skips_on_duplicate() {
        var services = new ServiceCollection();
        services.TryRegisterHostedService<TestHostedService>();

        var added = services.TryRegisterHostedService<TestHostedService>();

        await Assert.That(added).IsFalse();
        await Assert.That(services.Count(d => d.ServiceType == typeof(TestHostedService))).IsEqualTo(1);
    }

    [Test]
    public async ValueTask try_register_hosted_service_with_factory_uses_factory() {
        var services = new ServiceCollection();
        var instance = new TestHostedService();

        var added = services.TryRegisterHostedService(_ => instance);

        await Assert.That(added).IsTrue();
        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<TestHostedService>();
        await Assert.That(resolved).IsEqualTo(instance);
    }

    [Test]
    public async ValueTask try_register_hosted_service_survives_addhostedservice_dedup_trap() {
        // AddHostedService<T>(factory) dedups on (IHostedService, T) — second call is silently dropped.
        // TryRegisterHostedService uses AddSingleton<IHostedService>(factory), which bypasses the dedup,
        // but the concrete type IsRegistered<T> check still gates duplicate registration at the API level.
        var services = new ServiceCollection();

        var first  = services.TryRegisterHostedService<TestHostedService>();
        var second = services.TryRegisterHostedService<TestHostedService>();

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
    }

    #endregion // TryRegisterHostedService

    #region ->> Fixtures <<-

    interface IFoo { }

    sealed class Foo : IFoo {
        public static int ConstructedCount;

        public Foo() => Interlocked.Increment(ref ConstructedCount);
    }

    sealed class TestHostedService : IHostedService {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)  => Task.CompletedTask;
    }

    #endregion // Fixtures
}
