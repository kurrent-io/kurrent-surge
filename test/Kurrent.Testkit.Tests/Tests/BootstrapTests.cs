using Kurrent.Testing.Testkit;
using Kurrent.Testkit.TUnit;
using Microsoft.Extensions.Logging;

namespace Kurrent.Testkit.Tests;

[Category("Testkit")]
public class BootstrapTests {
    [Test]
    public async ValueTask test_id_is_available() {
        TestkitExecutionContext.Logger.LogInformation("TUnit Test instance identifier: {TestId}", TestContext.Current!.Id);
        await Assert.That(TestContext.Current.TestUid).IsNotEmptyGuid();
        await Assert.That(TestContext.Current.TestUid).IsEquivalentTo(Guid.Parse(TestContext.Current!.Id));
    }
}
