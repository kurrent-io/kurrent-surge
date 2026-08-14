using System.Runtime.ExceptionServices;

namespace Kurrent.Surge.Client;

/// <summary>
/// Runs the client's initialization tasks once, in order, after the first connection.
/// A failure in a task registered with throw-on-failure stops the chain and is rethrown
/// by <see cref="ThrowIfFaulted"/> from the fault gates on every gRPC call and backdoor
/// HTTP request.
/// </summary>
sealed partial class ClientInitializer(IReadOnlyList<InitializationTaskRegistration> registrations, ILogger logger) {
	volatile ExceptionDispatchInfo? _fault;

	/// <summary>Throws the stored initialization fault, if any, with its original stack.</summary>
	public void ThrowIfFaulted() => _fault?.Throw();

	public async Task Run(IKurrentClient client, CancellationToken ct) {
		foreach (var (task, throwOnFailure) in registrations) {
            LogExecutingInitializationTask(task.Name);

			var started = TimeProvider.System.GetTimestamp();

			try {
				await task.Execute(client, ct).ConfigureAwait(false);
				LogInitializationTaskCompleted(task.Name, TimeProvider.System.GetElapsedTime(started));
			}
			catch (OperationCanceledException) {
				// Cancellation is the client closing — skip the rest gracefully.
				LogClientInitializationCanceledRemainingTasksSkipped();
				return;
			}
			catch (Exception ex) when (!throwOnFailure) {
				LogInitializationTaskTaskFailed(task.Name, ex);
			}
			catch (Exception ex) {
				_fault = ExceptionDispatchInfo.Capture(ex);
				LogInitializationTaskTaskFailedTheClientIsFaulted(task.Name, ex);
				return;
			}
		}
	}

    [LoggerMessage(LogLevel.Debug, "Executing initialization task '{Task}'.")]
    partial void LogExecutingInitializationTask(string task);

    [LoggerMessage(LogLevel.Information, "Initialization task '{Task}' completed in {Elapsed}.")]
    partial void LogInitializationTaskCompleted(string task, TimeSpan elapsed);

    [LoggerMessage(LogLevel.Debug, "Client initialization canceled. Remaining tasks skipped.")]
    partial void LogClientInitializationCanceledRemainingTasksSkipped();

    [LoggerMessage(LogLevel.Warning, "Initialization task '{Task}' failed.")]
    partial void LogInitializationTaskTaskFailed(string task, Exception exception);

    [LoggerMessage(LogLevel.Error, "Initialization task '{Task}' failed. The client is faulted.")]
    partial void LogInitializationTaskTaskFailedTheClientIsFaulted(string task, Exception exception);
}
