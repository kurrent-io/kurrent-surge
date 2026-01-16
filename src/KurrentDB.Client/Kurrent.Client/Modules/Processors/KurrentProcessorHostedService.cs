using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams;

/// <summary>
/// Generic hosted service that runs a Kurrent processor.
/// Automatically registered when using <see cref="KurrentProcessorServiceCollectionExtensions.AddKurrentProcessor{TProcessor}"/>.
/// </summary>
/// <typeparam name="TProcessor">The processor type to host.</typeparam>
internal sealed partial class KurrentProcessorHostedService<TProcessor>(
    IProcessorWorker worker,
    ILogger<KurrentProcessorHostedService<TProcessor>> logger
) : BackgroundService
    where TProcessor : class, IRecordProcessor {

    /// <summary>
    /// Gets the underlying processor worker. Internal for testing purposes.
    /// </summary>
    internal IProcessorWorker Worker => worker;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LogStarting(worker.ProcessorId, worker.WorkerId);

        try {
            await worker.RunAsync(stoppingToken).ConfigureAwait(false);
            LogStopped(worker.ProcessorId, worker.WorkerId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            LogStopped(worker.ProcessorId, worker.WorkerId);
        }
        catch (Exception ex) {
            LogFailed(ex, worker.ProcessorId, worker.WorkerId);
            throw;
        }
        finally {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting processor {ProcessorId} worker {WorkerId}")]
    partial void LogStarting(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} stopped")]
    partial void LogStopped(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} worker {WorkerId} failed")]
    partial void LogFailed(Exception ex, string processorId, string workerId);
}
