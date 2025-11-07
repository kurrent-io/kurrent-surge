using System.Collections.Immutable;

namespace Kurrent.Client.Users;

/// <summary>
/// Fluent builder for constructing complex stream policy configurations.
/// </summary>
/// <remarks>
/// <para>
/// Provides a fluent API for building stream policy configurations with validation.
/// Use <see cref="WithDefaults"/> to start with standard policies, or create a new instance
/// for a custom configuration from scratch.
/// </para>
/// <para>
/// <b>Example:</b>
/// </para>
/// <code>
/// var config = StreamPolicyConfigurationBuilder.WithDefaults()
///     .AddPolicy("financePolicy", StreamAccessPolicy.FullAccess("financeTeam"))
///     .AddRule("finance-", "financePolicy")
///     .Build();
/// </code>
/// </remarks>
[PublicAPI]
public class StreamPolicyConfigurationBuilder {
    readonly Dictionary<string, StreamAccessPolicy> _policies = [];
    readonly List<StreamPrefixRule>                 _rules    = [];

    DefaultPolicyRules _defaults = DefaultPolicyRules.Standard;

    /// <summary>
    /// Creates a new builder starting with default configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default configuration includes:
    /// </para>
    /// <para>
    /// - publicDefault: Full access for all users
    /// </para>
    /// <para>
    /// - adminsDefault: Admin-only access
    /// </para>
    /// <para>
    /// - projectionsDefault: Public read, admin write for projection streams
    /// </para>
    /// <para>
    /// - Standard rules for projection streams ($et-, $ce-, $bc-, $category-, $streams)
    /// </para>
    /// </remarks>
    public static StreamPolicyConfigurationBuilder WithDefaults() {
        var builder = new StreamPolicyConfigurationBuilder {
            _policies = {
                ["publicDefault"]      = StreamAccessPolicy.PublicAccess,
                ["adminsDefault"]      = StreamAccessPolicy.AdminsOnly,
                ["projectionsDefault"] = StreamAccessPolicy.ProjectionsDefault
            }
        };

        builder._rules.AddRange([
            new StreamPrefixRule("$et-", "projectionsDefault"),
            new StreamPrefixRule("$ce-", "projectionsDefault"),
            new StreamPrefixRule("$bc-", "projectionsDefault"),
            new StreamPrefixRule("$category-", "projectionsDefault"),
            new StreamPrefixRule("$streams", "projectionsDefault")
        ]);

        return builder;
    }

    /// <summary>
    /// Creates a new empty builder.
    /// </summary>
    public StreamPolicyConfigurationBuilder() { }

    /// <summary>
    /// Adds or replaces a named policy.
    /// </summary>
    /// <param name="name">Policy name (referenced by rules).</param>
    /// <param name="policy">Access policy defining permissions.</param>
    public StreamPolicyConfigurationBuilder AddPolicy(string name, StreamAccessPolicy policy) {
        _policies[name] = policy;
        return this;
    }

    /// <summary>
    /// Adds a rule mapping a stream prefix to a policy.
    /// </summary>
    /// <param name="prefix">Stream name prefix (e.g., "finance-", "$et-").</param>
    /// <param name="policyName">Name of the policy to apply (must exist in policies).</param>
    public StreamPolicyConfigurationBuilder AddRule(string prefix, string policyName) {
        _rules.Add(new StreamPrefixRule(prefix, policyName));
        return this;
    }

    /// <summary>
    /// Sets the default policies for user and system streams.
    /// </summary>
    /// <param name="userStreamsPolicy">Policy name for user-created streams.</param>
    /// <param name="systemStreamsPolicy">Policy name for system streams.</param>
    public StreamPolicyConfigurationBuilder WithDefaultRules(string userStreamsPolicy, string systemStreamsPolicy) {
        _defaults = new DefaultPolicyRules {
            UserStreams   = userStreamsPolicy,
            SystemStreams = systemStreamsPolicy
        };
        return this;
    }

    /// <summary>
    /// Builds and validates the stream policy configuration.
    /// </summary>
    /// <returns>Validated stream policy configuration.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when configuration is invalid (e.g., rules reference non-existent policies).
    /// </exception>
    /// <remarks>
    /// This method performs validation before returning the configuration.
    /// Use <see cref="StreamPolicyConfiguration.Validate"/> for details on validation rules.
    /// </remarks>
    public StreamPolicyConfiguration Build() {
        var config = new StreamPolicyConfiguration {
            StreamPolicies = _policies.ToImmutableDictionary(),
            StreamRules    = [.._rules],
            DefaultRules   = _defaults
        };

        config.Validate();
        return config;
    }

    /// <summary>
    /// Removes a policy by name.
    /// </summary>
    /// <param name="name">Policy name to remove.</param>
    public StreamPolicyConfigurationBuilder RemovePolicy(string name) {
        _policies.Remove(name);
        return this;
    }

    /// <summary>
    /// Removes all rules matching the specified prefix.
    /// </summary>
    /// <param name="prefix">Stream prefix to remove rules for.</param>
    public StreamPolicyConfigurationBuilder RemoveRule(string prefix) {
        _rules.RemoveAll(r => r.Prefix == prefix);
        return this;
    }

    /// <summary>
    /// Removes all policies and rules.
    /// </summary>
    public StreamPolicyConfigurationBuilder Clear() {
        _policies.Clear();
        _rules.Clear();
        _defaults = DefaultPolicyRules.Standard;
        return this;
    }
}
