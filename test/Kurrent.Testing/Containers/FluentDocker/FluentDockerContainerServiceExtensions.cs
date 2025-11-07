using System.Net;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;

namespace Kurrent.Testing.Containers.FluentDocker;

public static class FluentDockerContainerServiceExtensions {
    extension(IContainerService service) {
        // IPAddress.Any defaults to IPAddress.Loopback
        public IPEndPoint GetPublicEndpoint(string portAndProtocol) {
            if (string.IsNullOrWhiteSpace(portAndProtocol))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(portAndProtocol));

            var endpoint = service.ToHostExposedEndpoint(portAndProtocol);
            return endpoint.Address.Equals(IPAddress.Any) ? new IPEndPoint(IPAddress.Loopback, endpoint.Port) : endpoint;
        }

        public IPEndPoint GetPublicEndpoint(int port) =>
            service.GetPublicEndpoint($"{port}/tcp");

        public CommandResponse<IList<string>> ExecuteCommand(string command) {
            var config = service.GetConfiguration();
            return service.DockerHost.Execute(config.Id, command, service.Certificates);
        }
    }

    extension(ContainerBuilder builder) {
        public ContainerBuilder WithPublicEndpointResolver() =>
            builder.UseCustomResolver((endpoints, portAndProtocol, dockerUri) => {
                var endpoint = endpoints.ToHostPort(portAndProtocol, dockerUri);
                return endpoint.Address.Equals(IPAddress.Any) ? new IPEndPoint(IPAddress.Loopback, endpoint.Port) : endpoint;
            });
    }
}
