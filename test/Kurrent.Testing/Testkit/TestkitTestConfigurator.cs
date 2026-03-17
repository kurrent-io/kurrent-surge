using Kurrent.Testing.OpenTelemetry;
using Kurrent.Testing.TUnit;
using TUnit.Core.Interfaces;

namespace Kurrent.Testing.Testkit;

public class TestkitTestConfigurator : Attribute, ITestDiscoveryEventReceiver {
    public ValueTask OnTestDiscovered(DiscoveredTestContext ctx) {
        var id = ctx.TestContext.InjectItem(Guid.NewGuid(), TestContextStateKeys.TestId);

        ctx.TestContext.ConfigureOtel(new OtelServiceMetadata(ctx.TestContext.Metadata.TestDetails.ClassType.Name) {
            ServiceInstanceId = id.ToString(),
            ServiceNamespace  = ctx.TestContext.Metadata.TestDetails.ClassType.Namespace
        });

        return ValueTask.CompletedTask;
    }
}

static class TestContextStateKeys {
    public const string TestId = "$TestId";
}

static class TestContextExtensions {
    extension(TestContext? ctx) {
        /// <summary>
        /// The unique identifier of the test, which is assigned during test discovery by the <see cref="TestkitTestConfigurator"/>.
        /// </summary>
        /// <remarks>
        /// Returns the test identifier, or <see cref="Guid.Empty"/> if the test context is null.
        /// </remarks>
        public Guid TestId => ctx.ExtractItem<Guid>(TestContextStateKeys.TestId) is var testId ? testId : Guid.Empty;
    }

    extension(Context? ctx) {
        /// <summary>
        /// The unique identifier of the test, which is assigned during test discovery by the <see cref="TestkitTestConfigurator"/>.
        /// </summary>
        /// <remarks>
        /// Returns the test identifier, or <see cref="Guid.Empty"/> if the test context is null.
        /// </remarks>
        public Guid TestId => ctx switch{
            TestContext testContext => testContext.TestId,
            _                       => Guid.Empty
        };
    }
}
