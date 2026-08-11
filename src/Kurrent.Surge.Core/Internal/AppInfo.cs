using System.Reflection;
using System.Runtime.InteropServices;

namespace Kurrent.Surge.Client;

/// <summary>
/// Identifies the running application and its process environment.
/// </summary>
class AppInfo {
    public static readonly AppInfo Current = GetCurrent();

    /// <summary>
    /// Gets the product name of the assembly.
    /// From <see cref="AssemblyProductAttribute"/>.
    /// </summary>
    public string? ProductName { get; private init; }

    /// <summary>
    /// Gets the product version string, typically the marketing version (e.g., "1.0.0-beta").
    /// From <see cref="AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    public string? ProductVersion { get; private init; }

    /// <summary>
    /// Gets the file version string (e.g., "1.0.0.42").
    /// From <see cref="AssemblyFileVersionAttribute"/>.
    /// </summary>
    public string? FileVersion { get; private init; }

    /// <summary>
    /// Gets the operating system name (e.g., windows, linux, darwin).
    /// </summary>
    public string OsName { get; } = GetOsName();

    /// <summary>
    /// Gets the operating system version (e.g., 10.0.22621).
    /// </summary>
    public string OsVersion { get; } = Environment.OSVersion.Version.ToString();

    /// <summary>
    /// Gets the runtime name (e.g., .NET).
    /// </summary>
    public string RuntimeName { get; } = ".NET";

    /// <summary>
    /// Gets the runtime version (e.g., 10.0.0).
    /// </summary>
    public string RuntimeVersion { get; } = Environment.Version.ToString();

    /// <summary>
    /// Gets the process CPU architecture (e.g., x64, arm64).
    /// </summary>
    public string HostArchitecture { get; } = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    static string GetOsName() =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "darwin"
        : OperatingSystem.IsLinux() ? "linux"
        : RuntimeInformation.OSDescription;

    static AppInfo GetCurrent() {
        var assembly = Assembly.GetExecutingAssembly();

        return new() {
            ProductName    = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product,
            ProductVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            FileVersion    = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
        };
    }
}
