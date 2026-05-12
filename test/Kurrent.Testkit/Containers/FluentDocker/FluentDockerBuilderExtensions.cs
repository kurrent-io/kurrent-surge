using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Contrib.WaitAndRetry;
using static System.TimeSpan;

namespace Kurrent.Testkit.Containers.FluentDocker;

[PublicAPI]
public static class FluentDockerContainerBuilderExtensions {
    extension(ContainerBuilder builder) {
        public ContainerBuilder WithEnvironment(Dictionary<string, string?> environment) =>
            builder.WithEnvironment(environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray());

        public ContainerBuilder WithEnvironment(IConfiguration configuration) =>
            builder.WithEnvironment(configuration.AsEnumerable()
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{x.Key}={x.Value}")
                .ToArray());

        public ContainerBuilder WaitUntilReady(IEnumerable<TimeSpan> sleepDurations, Action<IContainerService> action) =>
            builder.Wait("", (service, _) => {
                var result = Policy
                    .Handle<Exception>()
                    .WaitAndRetry(sleepDurations)
                    .ExecuteAndCapture(() => action(service));

                if (result.Outcome == OutcomeType.Successful)
                    return 0;

                throw result.FinalException is FluentDockerException
                    ? result.FinalException
                    : new FluentDockerException($"Service {service.Name} not ready: {result.FinalException.Message}");
            });

        public ContainerBuilder WaitUntilReadyWithConstantBackoff(
            TimeSpan delay, int retryCount, Action<IContainerService> action
        ) => builder.WaitUntilReady(Backoff.ConstantBackoff(delay, retryCount), action);

        public ContainerBuilder WaitUntilReadyWithExponentialBackoff(
            TimeSpan delay, int retryCount, Action<IContainerService> action
        ) => builder.WaitUntilReady(Backoff.ExponentialBackoff(delay, retryCount), action);

        public ContainerBuilder WaitUntilReadyWithConstantBackoff(
            int delayMs, int retryCount, Action<IContainerService> action
        ) => builder.WaitUntilReadyWithConstantBackoff(FromMilliseconds(delayMs), retryCount, action);

        public ContainerBuilder WaitUntilReadyWithExponentialBackoff(
            int delayMs, int retryCount, Action<IContainerService> action
        ) => builder.WaitUntilReadyWithExponentialBackoff(FromMilliseconds(delayMs), retryCount, action);

        public ContainerBuilder WaitUntilReadyAsync(IEnumerable<TimeSpan> sleepDurations, Func<IContainerService, ValueTask> action) =>
            builder.WaitUntilReady(sleepDurations, service => {
                var valueTask = action(service);
                if (!valueTask.IsCompletedSuccessfully)
                    valueTask.AsTask().GetAwaiter().GetResult();
            });

        public ContainerBuilder WaitUntilReadyWithConstantBackoffAsync(
            TimeSpan delay, int retryCount, Func<IContainerService, ValueTask> action
        ) => builder.WaitUntilReadyAsync(Backoff.ConstantBackoff(delay, retryCount, true), action);

        public ContainerBuilder WaitUntilReadyWithExponentialBackoffAsync(
            TimeSpan delay, int retryCount, Func<IContainerService, ValueTask> action
        ) => builder.WaitUntilReadyAsync(Backoff.ExponentialBackoff(delay, retryCount, fastFirst: true), action);

        public ContainerBuilder WaitUntilReadyWithConstantBackoffAsync(
            int delayMs, int retryCount, Func<IContainerService, ValueTask> action
        ) => builder.WaitUntilReadyWithConstantBackoffAsync(FromMilliseconds(delayMs), retryCount, action);

        public ContainerBuilder WaitUntilReadyWithExponentialBackoffAsync(
            int delayMs, int retryCount, Func<IContainerService, ValueTask> action
        ) => builder.WaitUntilReadyWithExponentialBackoffAsync(FromMilliseconds(delayMs), retryCount, action);
    }
}
