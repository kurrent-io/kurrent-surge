// PREDICATE-DRIVEN RETRY EXPERIMENT — preserved for review, not wired into the interceptor chain.
// The legacy channel retries via its declarative service-config policy instead (fixed status
// codes in KurrentDBClientRetrySettings). Disabled from compilation; it references a ShouldRetry
// predicate the legacy settings no longer carry. Re-enable by removing the #if false / #endif.
#if false

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace KurrentDB.Client.Interceptors;

/// <summary>
/// Predicate-driven retry for unary calls, replacing the channel's declarative service-config
/// retry policy: a gRPC RetryPolicy is data evaluated inside the channel and cannot invoke
/// <see cref="KurrentDBClientRetrySettings.ShouldRetry"/>, so the retry loop lives here instead.
/// Streaming calls pass through untouched — their resume semantics belong to the subscription
/// and read layers above.
/// </summary>
class RetryInterceptor(KurrentDBClientRetrySettings settings) : Interceptor {
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		TRequest request,
		ClientInterceptorContext<TRequest, TResponse> context,
		AsyncUnaryCallContinuation<TRequest, TResponse> continuation
	) {
		if (!settings.IsEnabled)
			return continuation(request, context);

		AsyncUnaryCall<TResponse>? current = null;

		var response = ExecuteWithRetry();

		// Headers, status, and trailers reflect the attempt that produced the final outcome; the
		// headers task awaits the retry loop first so it never observes an abandoned attempt.
		return new(
			response,
			AwaitHeaders(),
			() => current!.GetStatus(),
			() => current!.GetTrailers(),
			() => current?.Dispose());

		async Task<Grpc.Core.Metadata> AwaitHeaders() {
			await response.ConfigureAwait(false);
			return await current!.ResponseHeadersAsync.ConfigureAwait(false);
		}

		async Task<TResponse> ExecuteWithRetry() {
			var shouldRetry = settings.ShouldRetry ?? KurrentDBClientRetrySettings.DefaultShouldRetry;
			var maxAttempts = settings.MaxAttempts < 0 ? int.MaxValue : settings.MaxAttempts;
			var delay       = settings.InitialBackoff;
			var attempts    = 0;

			while (true) {
				current?.Dispose();
				current = continuation(request, context);

				try {
					return await current.ResponseAsync.ConfigureAwait(false);
				}
				catch (Exception ex) when (
					++attempts < maxAttempts &&
					!context.Options.CancellationToken.IsCancellationRequested &&
					shouldRetry(ex)
				) {
					await Task.Delay(delay, context.Options.CancellationToken).ConfigureAwait(false);

					// Plain exponential backoff; the replaced service-config policy added jitter,
					// but the legacy channel's retry volume does not warrant it.
					delay = TimeSpan.FromTicks(Math.Min(
						(long)(delay.Ticks * settings.BackoffMultiplier),
						settings.MaxBackoff.Ticks));
				}
			}
		}
	}
}

#endif
