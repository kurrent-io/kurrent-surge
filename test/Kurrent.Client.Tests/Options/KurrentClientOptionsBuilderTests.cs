using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Options;

public class KurrentClientOptionsBuilderTests {
    [Test]
    public void get_builder_returns_the_same_options() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder();

        var newOptions = builder.Build();

        newOptions.ShouldBeEquivalentTo(validOptions);
    }

    [Test]
    public void with_resilience_performs_complete_replacement() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder()
            .ConfigureResilience(KurrentClientResilienceOptions.NoResilience);

        var newOptions = builder.Build();

        // WithResilience performs complete replacement
        newOptions.Resilience.ShouldBeEquivalentTo(KurrentClientResilienceOptions.NoResilience);

        // Other properties should be preserved
        newOptions.ConnectionName.ShouldBe(validOptions.ConnectionName);
        newOptions.Endpoints.ShouldBe(validOptions.Endpoints);
        newOptions.Security.ShouldBeEquivalentTo(validOptions.Security);
        newOptions.Gossip.ShouldBeEquivalentTo(validOptions.Gossip);
    }

    [Test]
    public void with_messages_performs_complete_replacement() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false";

        var validOptions = KurrentClientOptions.Parse(connectionString);

        var builder = validOptions.GetBuilder()
            .ConfigureSchema(schema => schema
                .ConfigureMessages(options => options.Map<StreamMetadata>("$metadata")));

        var newOptions = builder.Build();

        // WithMessages performs complete replacement
        newOptions.Schema.Mapper.ShouldNotBe(validOptions.Schema.Mapper);
        newOptions.Schema.Mapper.TryGetSchemaName(typeof(StreamMetadata), out var schemaName).ShouldBeTrue();
        schemaName.Value.ShouldBe("$metadata");

        // Other properties should be preserved
        newOptions.ConnectionName.ShouldBe(validOptions.ConnectionName);
        newOptions.Endpoints.ShouldBe(validOptions.Endpoints);
        newOptions.Security.ShouldBeEquivalentTo(validOptions.Security);
        newOptions.Resilience.ShouldBeEquivalentTo(validOptions.Resilience);
    }

    [Test]
    public void configure_resilience_preserves_unmodified_properties() {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113/?tls=false&defaultDeadline=30000";

        var validOptions = KurrentClientOptions.Parse(connectionString);
        var originalDeadline = validOptions.Resilience.Deadline;

        var builder = validOptions.GetBuilder()
            .ConfigureResilience(res => res with {
                Retry = new KurrentClientResilienceOptions.RetryOptions { Enabled = false }
            });

        var newOptions = builder.Build();

        // ConfigureResilience preserves other properties
        newOptions.Resilience.Deadline.ShouldBe(originalDeadline);
        newOptions.Resilience.Retry.Enabled.ShouldBe(false);
        newOptions.Resilience.KeepAliveInterval.ShouldBe(validOptions.Resilience.KeepAliveInterval);
        newOptions.Resilience.KeepAliveTimeout.ShouldBe(validOptions.Resilience.KeepAliveTimeout);
    }
}
