using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Processors.Hosting;

/// <summary>
/// Wraps the host lifetime and rethrows a recorded processor failure once the host has stopped,
/// so <c>RunAsync</c> ends with the failure instead of returning.
/// </summary>
sealed class ProcessorFailureHostLifetime(IHostLifetime inner, ProcessorFailureRecorder recorder) : IHostLifetime {
    public Task WaitForStartAsync(CancellationToken cancellationToken) =>
        inner.WaitForStartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken) {
        await inner.StopAsync(cancellationToken).ConfigureAwait(false);

        if (recorder.Failure is { } failure)
            ExceptionDispatchInfo.Throw(failure);
    }
}
