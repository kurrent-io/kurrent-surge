namespace Kurrent.Client.Users;

/// <summary>
/// Maps a stream prefix to a named policy.
/// </summary>
/// <remarks>
/// Stream prefix rules determine which policy applies to streams based on their name prefix.
/// For example, a rule with prefix "finance-" and policy "financePolicy" applies the finance
/// policy to all streams whose names start with "finance-".
/// </remarks>
[PublicAPI]
public record StreamPrefixRule {
    /// <summary>
    /// Stream name prefix to match (e.g., "finance-", "$et-", "customer-").
    /// </summary>
    public string Prefix { get; init; }

    /// <summary>
    /// Name of the policy to apply to matching streams.
    /// </summary>
    /// <remarks>
    /// This must reference a policy defined in <see cref="StreamPolicyConfiguration.StreamPolicies"/>.
    /// </remarks>
    public string PolicyName { get; init; }

    /// <summary>
    /// Creates a new stream prefix rule.
    /// </summary>
    /// <param name="prefix">Stream name prefix to match.</param>
    /// <param name="policyName">Name of the policy to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when prefix or policyName is null.</exception>
    public StreamPrefixRule(string prefix, string policyName) {
        Prefix     = prefix ?? throw new ArgumentNullException(nameof(prefix));
        PolicyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
    }
}
