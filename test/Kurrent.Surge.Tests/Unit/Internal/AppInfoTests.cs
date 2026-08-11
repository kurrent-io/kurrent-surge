using System.Runtime.InteropServices;

namespace Kurrent.Surge.Tests.Internal;

public class ClientInfoTests {
    [Test]
    public async ValueTask exposes_the_current_platform_as_a_short_os_name() {
        // Arrange
        var expectedOsName = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "darwin"
            : OperatingSystem.IsLinux() ? "linux"
            : RuntimeInformation.OSDescription;

        // Act
        var info = ClientInfo.Current;

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
        var info = ClientInfo.Current;

        // Assert
        await Assert.That(info.HostArchitecture).IsEqualTo(expectedArchitecture);
        await Assert.That(info.RuntimeVersion).IsEqualTo(expectedRuntimeVersion);
        await Assert.That(info.OsVersion).IsEqualTo(expectedOsVersion);
        await Assert.That(info.RuntimeName).IsEqualTo(".NET");
    }

    [Test]
    public async ValueTask exposes_build_stamped_client_identity() {
        // Act
        var info = ClientInfo.Current;

        // Assert — GenerateAssemblyInfo guarantees both attributes; empty means a build defect
        await Assert.That(info.ClientName).IsNotEmpty();
        await Assert.That(info.ClientVersion).IsNotEmpty();
    }
}
