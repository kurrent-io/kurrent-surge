using System.Reflection;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Compose;

namespace Kurrent.Testkit.Containers.FluentDocker;

[PublicAPI]
public static class FluentDockerCompositeBuilderExtensions {
    extension(CompositeBuilder compositeBuilder) {
        public CompositeBuilder OverrideConfiguration(Action<DockerComposeConfig> configure) {
            configure(GetInternalConfig(compositeBuilder));
            return compositeBuilder;

            static DockerComposeConfig GetInternalConfig(CompositeBuilder compositeBuilder) =>
                (DockerComposeConfig)typeof(CompositeBuilder)
                    .GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(compositeBuilder)!;
        }
    }
}
