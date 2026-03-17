using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Kurrent.Testing.Configuration;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Configuration;
using Serilog;

using static System.StringComparison;

namespace Kurrent.Testing.Containers.FluentDocker;

[PublicAPI]
public abstract class TestContainer {
    protected ILogger Logger { get; private set; }

    static TestContainer() => Ductus.FluentDocker.Services.Logging.Enabled();

    /// <summary>
    /// Represents an abstract base class for managing and interacting with test containers,
    /// including initialization, configuration, and lifecycle management.
    /// </summary>
    /// <param name="defaultImage">
    /// The default Docker image to use for the container.
    /// </param>
    /// <param name="serviceName">
    /// An optional service name for the container, used for configuration and logging.
    /// If not provided, it will be derived from the class name.
    /// </param>
    protected TestContainer(string defaultImage, string? serviceName = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultImage);

        ServiceName = GetServiceName(serviceName ?? GetType().Name);

        try {
            Logger = Log
                .ForContext(Serilog.Core.Constants.SourceContextPropertyName, GetType().Name)
                .ForContext(nameof(ServiceName), ServiceName);

            var settings = new TestContainerSettings { Image = defaultImage };

            TestkitExecutionContext.Configuration
                .Bind($"TestContainers:{ServiceName}", settings);

            Image   = settings.Image;
            Enabled = settings.Enabled;

            Logger.Information("Container settings loaded: {@Settings}", settings);

            // Logger.Information("Container initialized. Enabled: {Enabled} Image: {Image}", Enabled, Image);
        } catch (Exception ex) {
            throw new FluentDockerException($"Failed to initialize container {ServiceName} container", ex);
        }

        return;

        static string GetServiceName(string serviceName) =>
            serviceName
                .Replace("TestContainer", "", OrdinalIgnoreCase)
                .Replace("Container", "", OrdinalIgnoreCase)
                .Trim();
    }

    public string ServiceName { get; }
    public string Image       { get; }
    public bool   Enabled     { get; }

    public IContainerService Service { get; private set; } = null!;

    /// <summary>
    /// Gets the current container configuration.
    /// This is an expensive operation that queries Docker, use sparingly.
    /// </summary>
    /// <param name="fresh">If true, forces a fresh query to Docker instead of using cached data.</param>
    public Container GetConfiguration(bool fresh = true) => Service.GetConfiguration(fresh);

    protected abstract ContainerBuilder ConfigureContainer(ContainerBuilder builder);

    public async ValueTask Start() {
        if (!Enabled) {
            Logger.Warning("Container is disabled, skipping start");
            return;
        }

        if (Service is { State: ServiceRunningState.Running }) {
            Logger.Warning("Container already running, skipping start");
            return;
        }

        try {
            Service = ConfigureContainer(
                new Builder()
                    .UseContainer()
                    .WithName(ServiceName)
                    .UseImage(Image)
                    .WithPublicEndpointResolver()
            ).Build();

            // foreach (var serviceState in Enum.GetValues<ServiceRunningState>()) {
            //     Service.AddHook(
            //         state: serviceState,
            //         hook: service => Logger.Verbose("{ServiceName} State:{ServiceState}", service.Name, serviceState),
            //         uniqueName: $"{Service.Name}-{serviceState}".ToLowerInvariant()
            //     );
            // }

            await Task.Run(() => Service.Start()).ConfigureAwait(false);

            Logger.Information(
                "Container started: {@ExposedPorts}",
                Service.GetConfiguration().Config.ExposedPorts.Keys);
        }
        catch (Exception ex) {
            Logger.Error(ex, "Failed to start container");
            throw new FluentDockerException($"Failed to start {ServiceName} container", ex);
        }

        try {
            await OnStarted().ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} {nameof(OnStarted)} Execution error", ex);
        }
    }

    public async ValueTask Stop() {
        if (Service is not { State: ServiceRunningState.Running }) {
            Logger.Warning("Container not running, skipping stop");
            return;
        }

        try {
            Service.Stop();
            Logger.Information("Container stopped");
        }
        catch (Exception ex) {
            throw new FluentDockerException($"Failed to stop {ServiceName} container", ex);
        }

        try {
            await OnStopped().ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} {nameof(OnStopped)} Execution error", ex);
        }
    }

    public void ReportStatus() {
        var cfg = GetConfiguration(fresh: true);

        Logger.Debug(
            "Container {ContainerName} Created On: {CreatedOn} Exposed Ports: {@ExposedPorts}",
            cfg.Name, cfg.Created, cfg.Config.ExposedPorts.Keys
        );
    }

    public virtual ValueTask DisposeAsync() {
        try {
            Service.Dispose();
            Logger.Debug("{ServiceName} Container service disposed successfully", ServiceName);
        }
        catch (Exception ex) {
            throw new FluentDockerException($"Failed to dispose {ServiceName} container", ex);
        }

        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnStarted() => ValueTask.CompletedTask;
    protected virtual ValueTask OnStopped() => ValueTask.CompletedTask;
}

/// <summary>
/// Represents settings for configuring and controlling test containers.
/// </summary>
public record TestContainerSettings {
    /// <summary>
    /// The Docker image to use for the container. This should be a valid image name that can be pulled from a Docker registry.
    /// </summary>
    public string Image { get; init; } = "";

    /// <summary>
    /// Indicates whether the container should be enabled and started during testing.
    /// If set to false, the container will not be started.
    /// </summary>
    public bool Enabled { get; init; }
}
