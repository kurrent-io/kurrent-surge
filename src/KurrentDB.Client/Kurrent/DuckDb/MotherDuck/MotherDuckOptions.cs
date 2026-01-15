namespace Kurrent.DuckDB;

/// <summary>
/// Configuration options for MotherDuck cloud connections.
/// </summary>
public sealed record MotherDuckOptions {
    /// <summary>
    /// Default MotherDuck options (not configured).
    /// </summary>
    public static readonly MotherDuckOptions Default = new();

    /// <summary>
    /// Gets the MotherDuck database name.
    /// </summary>
    public string Database { get; init; } = "";

    /// <summary>
    /// Gets the authentication token.
    /// </summary>
    public string Token { get; init; } = "";

    /// <summary>
    /// Gets whether this options instance has been configured with a database.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Database);
}
