using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Options;

public class KurrentClientOptionsBuilderTests {
    [Test]
    public async Task get_builder_returns_the_same_options() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder();

        var newOptions = builder.Build();

        await Assert.That(newOptions).IsDeepEquivalentTo(validOptions);
    }

    [Test]
    public async Task with_resilience_performs_complete_replacement() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder()
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience);

        var newOptions = builder.Build();

        // WithResilience performs complete replacement
        await Assert.That(newOptions.Resilience).IsDeepEquivalentTo(KurrentClientResilienceOptions.NoResilience);

        // Other properties should be preserved
        await Assert.That(newOptions.ConnectionName).IsEqualTo(validOptions.ConnectionName);
        await Assert.That(newOptions.Endpoints).IsEqualTo(validOptions.Endpoints);
        await Assert.That(newOptions.Security).IsDeepEquivalentTo(validOptions.Security);
        await Assert.That(newOptions.Gossip).IsDeepEquivalentTo(validOptions.Gossip);
    }

    [Test]
    public async Task with_messages_performs_complete_replacement() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder()
            .ConfigureSchema(schema => schema
                .ConfigureMessages(options => options.Map<StreamMetadata>("$metadata")));

        var newOptions = builder.Build();

        // WithMessages performs complete replacement
        await Assert.That(newOptions.Schema.Mapper).IsNotEqualTo(validOptions.Schema.Mapper);
        await Assert.That(newOptions.Schema.Mapper.TryGetSchemaName(typeof(StreamMetadata), out var schemaName)).IsTrue();
        await Assert.That(schemaName.Value).IsEqualTo("$metadata");

        // Other properties should be preserved
        await Assert.That(newOptions.ConnectionName).IsEqualTo(validOptions.ConnectionName);
        await Assert.That(newOptions.Endpoints).IsEqualTo(validOptions.Endpoints);
        await Assert.That(newOptions.Security).IsDeepEquivalentTo(validOptions.Security);
        await Assert.That(newOptions.Resilience).IsDeepEquivalentTo(validOptions.Resilience);
    }

    [Test]
    public async Task configure_resilience_preserves_unmodified_properties() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false&defaultDeadline=30000";

        var validOptions = KurrentClientOptions.Parse(connectionString);
        var originalDeadline = validOptions.Resilience.Deadline;

        var builder = validOptions.GetBuilder()
            .ConfigureResilience(res => res with {
                Retry = new KurrentClientResilienceOptions.RetryOptions { Enabled = false }
            });

        var newOptions = builder.Build();

        // ConfigureResilience preserves other properties
        await Assert.That(newOptions.Resilience.Deadline).IsEqualTo(originalDeadline);
        await Assert.That(newOptions.Resilience.Retry.Enabled).IsEqualTo(false);
        await Assert.That(newOptions.Resilience.KeepAliveInterval).IsEqualTo(validOptions.Resilience.KeepAliveInterval);
        await Assert.That(newOptions.Resilience.KeepAliveTimeout).IsEqualTo(validOptions.Resilience.KeepAliveTimeout);
    }
}
