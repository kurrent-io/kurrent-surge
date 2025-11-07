namespace Kurrent.Client.Users;

/// <summary>
/// Default fallback policies for user-created and system streams.
/// </summary>
/// <remarks>
/// <para>
/// When no specific prefix rule matches a stream, the default policy rules determine
/// which policy applies based on whether the stream is a user stream or system stream.
/// </para>
/// <para>
/// System streams are identified by names starting with the dollar sign ($).
/// </para>
/// </remarks>
[PublicAPI]
public record DefaultPolicyRules {
    /// <summary>
    /// Policy name for user-created streams (streams not starting with $).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> "publicDefault"</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>This policy applies to all user-created streams that don't match any specific prefix rule.</para>
    /// </remarks>
    public string UserStreams { get; init; } = "publicDefault";

    /// <summary>
    /// Policy name for system streams (streams starting with $).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> "adminsDefault"</para>
    /// <para><b>Importance:</b> High</para>
    /// <para>This policy applies to all system streams that don't match any specific prefix rule.</para>
    /// </remarks>
    public string SystemStreams { get; init; } = "adminsDefault";

    /// <summary>
    /// Standard default rules with public access for user streams and admin-only for system streams.
    /// </summary>
    public static readonly DefaultPolicyRules Standard = new();
}
