using Kurrent.Testing.OpenTelemetry;
using TUnit.Core.Interfaces;

namespace Kurrent.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class TestkitTestConfiguratorAttribute : Attribute, ITestDiscoveryEventReceiver {
    public ValueTask OnTestDiscovered(DiscoveredTestContext context) {
        context.TestContext.ConfigureOtel(new OtelServiceMetadata(context.TestContext.Metadata.TestDetails.ClassType.Name) {
            ServiceInstanceId = context.TestContext.Id.ToString("N"),
            ServiceNamespace  = context.TestContext.Metadata.TestDetails.ClassType.Namespace
        });

        return ValueTask.CompletedTask;
    }
}
