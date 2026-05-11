// ReSharper disable CheckNamespace

namespace Kurrent.Testkit.TUnit;

public static class TestContextExtensions {
    extension(TestContext? testContext) {
        /// <summary>
        /// Returns the unique identifier of the test context as a typed <see cref="Guid"/>.
        /// </summary>
        /// <remarks>
        /// Named TestUid to avoid conflicts with the TUnit TestDetails.TestId property.
        /// </remarks>
        public Guid TestUid => Guid.Parse(testContext!.Id);
    }
}
