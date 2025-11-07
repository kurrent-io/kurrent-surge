namespace Kurrent.Client.Users;

/// <summary>
/// Authorization mechanism for stream access control.
/// </summary>
[PublicAPI]
public enum AuthorizationType {
    /// <summary>
    /// Stream Policy Authorization using fine-grained prefix-based control.
    /// </summary>
    /// <remarks>
    /// Policies define access rules based on stream name prefixes, allowing centralized
    /// management of permissions for groups of streams.
    /// </remarks>
    StreamPolicy,

    /// <summary>
    /// Access Control Lists using per-stream ACL metadata.
    /// </summary>
    /// <remarks>
    /// Traditional ACL-based authorization where each stream's metadata contains
    /// access control information for that specific stream.
    /// </remarks>
    Acl
}
