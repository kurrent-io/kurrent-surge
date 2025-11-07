using System.Collections.Immutable;
using Kurrent.Client.Testing.Shouldly;
using Kurrent.Client.Users;

namespace Kurrent.Client.Tests.Users;

[Category("UserManagement")]
[Category("StreamPolicy")]
public class StreamPolicyTests : KurrentClientTestFixture {
    [Test]
    public async Task enables_stream_policies_successfully() {
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Verify default policy is created by reading it back
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync(config => {
                config.StreamPolicies.ShouldContainKey("publicDefault");
                config.StreamPolicies.ShouldContainKey("adminsDefault");
                config.StreamPolicies.ShouldContainKey("projectionsDefault");
            });
    }

    [Test]
    public async Task disables_stream_policies_successfully() {
        // First enable
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Then disable
        await AutomaticClient.Users
            .DisableStreamPolicies()
            .ShouldNotThrowOrFailAsync();
    }

    [Test]
    public async Task configures_stream_policies_with_custom_policy() {
        // First enable stream policies
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Create custom configuration
        var config = new StreamPolicyConfiguration {
            StreamPolicies = ImmutableDictionary<string, StreamAccessPolicy>.Empty
                .Add("publicDefault", StreamAccessPolicy.PublicAccess)
                .Add("adminsDefault", StreamAccessPolicy.AdminsOnly)
                .Add("projectionsDefault", StreamAccessPolicy.ProjectionsDefault)
                .Add("financePolicy", StreamAccessPolicy.FullAccess("financeTeam", "ouro")),
            StreamRules = ImmutableArray.Create(
                new StreamPrefixRule("finance-", "financePolicy"),
                new StreamPrefixRule("$et-", "projectionsDefault"),
                new StreamPrefixRule("$ce-", "projectionsDefault"),
                new StreamPrefixRule("$bc-", "projectionsDefault"),
                new StreamPrefixRule("$category-", "projectionsDefault"),
                new StreamPrefixRule("$streams", "projectionsDefault")
            ),
            DefaultRules = DefaultPolicyRules.Standard
        };

        await AutomaticClient.Users
            .ConfigureStreamPolicies(config)
            .ShouldNotThrowOrFailAsync();

        // Verify configuration was applied
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync(retrievedConfig => {
                retrievedConfig.StreamPolicies.ShouldContainKey("financePolicy");
                retrievedConfig.StreamPolicies["financePolicy"].Readers.ShouldContain("financeTeam");
                retrievedConfig.StreamPolicies["financePolicy"].Readers.ShouldContain("ouro");
            });
    }

    [Test]
    public async Task get_current_policy_returns_not_found_when_no_policy_exists() {
        // Ensure we're in ACL mode (no stream policies)
        await AutomaticClient.Users
            .DisableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Try to get policy - should fail with NotFound
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldFailAsync(failure =>
                failure.Case.ShouldBe(GetStreamPoliciesError.GetStreamPoliciesErrorCase.NotFound));
    }

    [Test]
    public async Task validates_configuration_before_sending() {
        // Create invalid configuration (rule references non-existent policy)
        var invalidConfig = new StreamPolicyConfiguration {
            StreamPolicies = ImmutableDictionary<string, StreamAccessPolicy>.Empty
                .Add("publicDefault", StreamAccessPolicy.PublicAccess),
            StreamRules = ImmutableArray.Create(
                new StreamPrefixRule("finance-", "nonExistentPolicy") // Invalid reference
            ),
            DefaultRules = new DefaultPolicyRules {
                UserStreams   = "publicDefault",
                SystemStreams = "publicDefault"
            }
        };

        // Should throw ArgumentException due to validation
        var exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await AutomaticClient.Users.ConfigureStreamPolicies(invalidConfig)
        );

        exception.Message.ShouldContain("nonExistentPolicy");
    }

    [Test]
    public async Task skips_validation_when_requested() {
        // First enable stream policies
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Create configuration that would fail validation
        var config = new StreamPolicyConfiguration {
            StreamPolicies = ImmutableDictionary<string, StreamAccessPolicy>.Empty
                .Add("publicDefault", StreamAccessPolicy.PublicAccess),
            StreamRules = ImmutableArray.Create(
                new StreamPrefixRule("test-", "nonExistentPolicy")
            ),
            DefaultRules = new DefaultPolicyRules {
                UserStreams   = "publicDefault",
                SystemStreams = "publicDefault"
            }
        };

        // Should fail on server side, not client validation
        await AutomaticClient.Users
            .ConfigureStreamPolicies(config, validate: false)
            .ShouldFailAsync(failure =>
                failure.Case.ShouldBe(ConfigureStreamPoliciesError.ConfigureStreamPoliciesErrorCase.BadRequest));
    }

    [Test]
    public async Task uses_default_configuration_successfully() {
        // First enable stream policies
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Use the default configuration
        await AutomaticClient.Users
            .ConfigureStreamPolicies(StreamPolicyConfiguration.Default)
            .ShouldNotThrowOrFailAsync();

        // Verify it was applied
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync(config => {
                config.StreamPolicies.Count.ShouldBe(3);
                config.StreamPolicies.ShouldContainKey("publicDefault");
                config.StreamPolicies.ShouldContainKey("adminsDefault");
                config.StreamPolicies.ShouldContainKey("projectionsDefault");
            });
    }

    [Test]
    public async Task fluent_api_with_stream_access_policy() {
        // First enable stream policies
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        // Use fluent API to create custom policy
        var customPolicy = StreamAccessPolicy.PublicAccess
            .WithWriters("admin", "financeTeam")
            .WithDeleters("admin");

        var config = new StreamPolicyConfiguration {
            StreamPolicies = ImmutableDictionary<string, StreamAccessPolicy>.Empty
                .Add("publicDefault", StreamAccessPolicy.PublicAccess)
                .Add("adminsDefault", StreamAccessPolicy.AdminsOnly)
                .Add("projectionsDefault", StreamAccessPolicy.ProjectionsDefault)
                .Add("customPolicy", customPolicy),
            StreamRules = ImmutableArray.Create(
                new StreamPrefixRule("custom-", "customPolicy"),
                new StreamPrefixRule("$et-", "projectionsDefault"),
                new StreamPrefixRule("$ce-", "projectionsDefault"),
                new StreamPrefixRule("$bc-", "projectionsDefault"),
                new StreamPrefixRule("$category-", "projectionsDefault"),
                new StreamPrefixRule("$streams", "projectionsDefault")
            ),
            DefaultRules = DefaultPolicyRules.Standard
        };

        await AutomaticClient.Users
            .ConfigureStreamPolicies(config)
            .ShouldNotThrowOrFailAsync();

        // Verify the fluent configuration
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync(retrievedConfig => {
                retrievedConfig.StreamPolicies["customPolicy"].Readers.ShouldBe(["$all"]);
                retrievedConfig.StreamPolicies["customPolicy"].Writers.ShouldBe(["admin", "financeTeam"]);
                retrievedConfig.StreamPolicies["customPolicy"].Deleters.ShouldBe(["admin"]);
            });
    }

    [Test]
    public async Task updates_existing_policy_configuration() {
        // First enable and set initial config
        await AutomaticClient.Users
            .EnableStreamPolicies()
            .ShouldNotThrowOrFailAsync();

        var initialConfig = StreamPolicyConfiguration.Default;
        await AutomaticClient.Users
            .ConfigureStreamPolicies(initialConfig)
            .ShouldNotThrowOrFailAsync();

        // Get current config
        var currentResult = await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync();

        // Modify and update
        var updatedConfig = currentResult.Value with {
            StreamPolicies = currentResult.Value.StreamPolicies
                .Add("newPolicy", StreamAccessPolicy.FullAccess("newUser")),
            StreamRules = currentResult.Value.StreamRules
                .Add(new StreamPrefixRule("new-", "newPolicy"))
        };

        await AutomaticClient.Users
            .ConfigureStreamPolicies(updatedConfig)
            .ShouldNotThrowOrFailAsync();

        // Verify update
        await AutomaticClient.Users
            .GetCurrentStreamPolicy()
            .ShouldNotThrowOrFailAsync(config => {
                config.StreamPolicies.ShouldContainKey("newPolicy");
                config.StreamRules.ShouldContain(r => r.Prefix == "new-");
            });
    }
}
