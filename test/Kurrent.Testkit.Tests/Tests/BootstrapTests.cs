namespace Kurrent.Testkit.Tests;

[Category("Testkit")]
public class BootstrapTests {
    [Test]
    public async ValueTask test_id_is_set() =>
        await Assert.That(TestContext.Current.TestId).IsNotDefault();
}
