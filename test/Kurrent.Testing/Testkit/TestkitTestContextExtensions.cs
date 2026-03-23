// ReSharper disable CheckNamespace

using System.Text;

namespace Kurrent.Testkit;

public static class TestUidTestContextExtensions {
    static readonly DeterministicGuidGenerator Generator =
        new(Guid.Parse("92BBC7B6-8921-4C35-A255-F1ED21B5EDEA"));

    extension(TestContext? testContext) {
        public Guid TestId {
            get {
                ArgumentNullException.ThrowIfNull(testContext);
                return Generator.Generate(Encoding.UTF8.GetBytes(testContext.Metadata.TestName));
            }
        }
    }
}
