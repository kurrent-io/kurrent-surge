using System.Runtime.InteropServices;

namespace Kurrent.Surge.Tests.Internal;

public class AppInfoTests {
    [Test]
    public async ValueTask exposes_the_current_platform_as_a_short_os_name() {
        // Arrange
        var expectedOsName = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "darwin"
            : OperatingSystem.IsLinux() ? "linux"
            : RuntimeInformation.OSDescription;

        // Act
        var info = AppInfo.Current;

        // Assert
        await Assert.That(info.OsName).IsEqualTo(expectedOsName);
    }

    [Test]
    public async ValueTask exposes_process_environment_information() {
        // Arrange
        var expectedArchitecture   = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var expectedRuntimeVersion = Environment.Version.ToString();
        var expectedOsVersion      = Environment.OSVersion.Version.ToString();

        // Act
        var info = AppInfo.Current;

        // Assert
        await Assert.That(info.HostArchitecture).IsEqualTo(expectedArchitecture);
        await Assert.That(info.RuntimeVersion).IsEqualTo(expectedRuntimeVersion);
        await Assert.That(info.OsVersion).IsEqualTo(expectedOsVersion);
        await Assert.That(info.RuntimeName).IsEqualTo(".NET");
    }
}
