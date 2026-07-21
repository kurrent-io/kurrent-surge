namespace Kurrent.Surge.AI.HuggingFace.Tests;

public class HardwareProbeTests {
    [Test]
    public async ValueTask reports_at_least_one_arch_tag_for_the_running_host() {
        // Pure detection: any supported test host is either arm64 or at least avx2-class x86.
        var tags = HardwareProbe.PreferredArchTags();

        await Assert.That(tags.Count).IsGreaterThan(0);
    }
}
