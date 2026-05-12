using System.Reflection;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace Kurrent.Testkit.Containers.FluentDocker;

[PublicAPI]
public static class FluentDockerServiceExtensions {
	static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);

    extension(ICompositeService service) {
        public DockerComposeConfig Configuration() =>
            (DockerComposeConfig)typeof(DockerComposeCompositeService)
                .GetProperty("Config", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(service)!;

        public async Task WaitUntilNodesAreHealthy(IEnumerable<string> services, CancellationToken cancellationToken) {
            var nodes = service.Containers.Where(x => services.Contains(x.Name));

            await Parallel.ForEachAsync(nodes, cancellationToken, async (node, ct) => await node.WaitUntilNodesAreHealthy(ct));
        }

        public async Task WaitUntilNodesAreHealthy(string serviceNamePrefix, CancellationToken cancellationToken) {
            var nodes = service.Containers.Where(x => x.Name.StartsWith(serviceNamePrefix));
            await Parallel.ForEachAsync(nodes, cancellationToken, async (node, ct) => await node.WaitUntilNodesAreHealthy(ct));
        }

        public async Task WaitUntilNodesAreHealthy(string serviceNamePrefix, TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            await WaitUntilNodesAreHealthy(service, serviceNamePrefix, cts.Token);
        }
    }

    extension(IContainerService service) {
        public async Task WaitUntilNodesAreHealthy(CancellationToken cancellationToken) {
            while (true) {
                var config = service.GetConfiguration(true);
                var status = config?.State?.Health?.Status;

                if (status is HealthState.Healthy) return;

                if (cancellationToken.IsCancellationRequested)
                    throw new FluentDockerException($"Wait for healthy expired for container {service.Id}");

                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(DefaultRetryDelay);
            }
        }

        public async ValueTask WaitUntilNodesAreHealthy(TimeSpan timeout) {
            using var cts = new CancellationTokenSource(timeout);
            await WaitUntilNodesAreHealthy(service, cts.Token);
        }
    }
}
