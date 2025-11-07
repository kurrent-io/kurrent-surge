using System.Collections.Immutable;

namespace Kurrent.Client.Users;

/// <summary>
/// Complete stream policy configuration including policy definitions, stream rules, and defaults.
/// </summary>
/// <remarks>
/// <para>
/// Stream policy configuration defines access control for streams based on their name prefixes.
/// It consists of three parts: named policies, prefix rules, and default rules.
/// </para>
/// <para>
/// <b>Usage:</b> Create a configuration, validate it, and send to the server using
/// <see cref="UsersClient.ConfigureStreamPolicies"/>.
/// </para>
/// </remarks>
[PublicAPI]
public record StreamPolicyConfiguration {
    /// <summary>
    /// Named access policies defining permissions for users and groups.
    /// </summary>
    /// <remarks>
    /// Dictionary mapping policy names to their access definitions. Policy names are referenced
    /// by stream rules and default rules to apply permissions to streams.
    /// </remarks>
    public IReadOnlyDictionary<string, StreamAccessPolicy> StreamPolicies { get; init; }
        = ImmutableDictionary<string, StreamAccessPolicy>.Empty;

    /// <summary>
    /// Rules mapping stream prefixes to specific policies.
    /// </summary>
    /// <remarks>
    /// These rules are evaluated in order. The first rule whose prefix matches a stream name
    /// determines which policy applies to that stream.
    /// </remarks>
    public IReadOnlyList<StreamPrefixRule> StreamRules { get; init; }
        = ImmutableArray<StreamPrefixRule>.Empty;

    /// <summary>
    /// Default policies for user and system streams.
    /// </summary>
    /// <remarks>
    /// Applied to streams that don't match any specific prefix rule.
    /// </remarks>
    public DefaultPolicyRules DefaultRules { get; init; } = DefaultPolicyRules.Standard;

    /// <summary>
    /// Pre-configured policy with public user access and admin-only system streams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This default configuration includes:
    /// </para>
    /// <para>
    /// - publicDefault: Full access for all users
    /// </para>
    /// <para>
    /// - adminsDefault: Admin-only access for system streams
    /// </para>
    /// <para>
    /// - projectionsDefault: Public read, admin write for projection streams
    /// </para>
    /// <para>
    /// - Rules for standard projection streams ($et-, $ce-, $bc-, $category-, $streams)
    /// </para>
    /// </remarks>
    public static readonly StreamPolicyConfiguration Default = new() {
        StreamPolicies = ImmutableDictionary<string, StreamAccessPolicy>.Empty
            .Add("publicDefault", StreamAccessPolicy.PublicAccess)
            .Add("adminsDefault", StreamAccessPolicy.AdminsOnly)
            .Add("projectionsDefault", StreamAccessPolicy.ProjectionsDefault),
        StreamRules = ImmutableArray.Create(
            new StreamPrefixRule("$et-", "projectionsDefault"),
            new StreamPrefixRule("$ce-", "projectionsDefault"),
            new StreamPrefixRule("$bc-", "projectionsDefault"),
            new StreamPrefixRule("$category-", "projectionsDefault"),
            new StreamPrefixRule("$streams", "projectionsDefault")
        ),
        DefaultRules = DefaultPolicyRules.Standard
    };

    /// <summary>
    /// Validates the configuration before sending to the server.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when policy references are invalid (e.g., a rule references a non-existent policy).
    /// </exception>
    /// <remarks>
    /// Validation checks:
    /// <list type="bullet">
    /// <item>All stream rules reference existing policies</item>
    /// <item>Default rules reference existing policies</item>
    /// <item>Policy names are not null or empty</item>
    /// </list>
    /// </remarks>
    public void Validate() {
        // Check stream rules reference existing policies
        foreach (var rule in StreamRules) {
            if (string.IsNullOrWhiteSpace(rule.Prefix))
                throw new ArgumentException($"Stream rule has empty prefix");

            if (string.IsNullOrWhiteSpace(rule.PolicyName))
                throw new ArgumentException($"Stream rule for prefix '{rule.Prefix}' has empty policy name");

            if (!StreamPolicies.ContainsKey(rule.PolicyName))
                throw new ArgumentException($"Stream rule for prefix '{rule.Prefix}' references undefined policy: '{rule.PolicyName}'");
        }

        // Check default rules reference existing policies
        if (string.IsNullOrWhiteSpace(DefaultRules.UserStreams))
            throw new ArgumentException("Default user streams policy name is empty");

        if (!StreamPolicies.ContainsKey(DefaultRules.UserStreams))
            throw new ArgumentException($"Default user streams rule references undefined policy: '{DefaultRules.UserStreams}'");

        if (string.IsNullOrWhiteSpace(DefaultRules.SystemStreams))
            throw new ArgumentException("Default system streams policy name is empty");

        if (!StreamPolicies.ContainsKey(DefaultRules.SystemStreams))
            throw new ArgumentException($"Default system streams rule references undefined policy: '{DefaultRules.SystemStreams}'");
    }
}
