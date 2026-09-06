using System.Reflection;
using System.Runtime.InteropServices;

namespace Kurrent.Surge.Client;

/// <summary>
/// Identifies the client and its process environment.
/// </summary>
sealed record ClientProcessInfo {
    public static readonly ClientProcessInfo Current = GetCurrent();

    /// <summary>
    /// Gets the client name.
    /// From <see cref="AssemblyProductAttribute"/>.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// Gets the client version (e.g., "1.0.0-beta").
    /// From <see cref="AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    public required string ClientVersion { get; init; }

    /// <summary>
    /// Gets the operating system name (e.g., windows, linux, darwin).
    /// </summary>
    public required string OsName { get; init; }

    /// <summary>
    /// Gets the operating system version (e.g., 10.0.22621).
    /// </summary>
    public required string OsVersion { get; init; }

    /// <summary>
    /// Gets the runtime name (e.g., .NET).
    /// </summary>
    public required string RuntimeName { get; init; }

    /// <summary>
    /// Gets the runtime version (e.g., 10.0.0).
    /// </summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>
    /// Gets the process CPU architecture (e.g., x64, arm64).
    /// </summary>
    public required string HostArchitecture { get; init; }

    static ClientProcessInfo GetCurrent() {
        var assembly = Assembly.GetExecutingAssembly();

        // GenerateAssemblyInfo is on (Directory.Build.props), so the build always stamps
        // both attributes — absence is a build defect and must fail loudly here.
        return new() {
            ClientName    = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product,
            ClientVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,

            OsName           = GetOsName(),
            OsVersion        = Environment.OSVersion.Version.ToString(),
            RuntimeName      = ".NET",
            RuntimeVersion   = Environment.Version.ToString(),
            HostArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        static string GetOsName() =>
            OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "darwin"
            : OperatingSystem.IsLinux() ? "linux"
            : RuntimeInformation.OSDescription;
    }
}
