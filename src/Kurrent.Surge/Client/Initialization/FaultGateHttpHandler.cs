namespace Kurrent.Surge.Client;

/// <summary>
/// Enforces the initialization fault on every backdoor HTTP request: a faulted client
/// makes no calls. Sits at the head of the factory's shared handler chain, so caching
/// the <see cref="HttpClient"/> cannot bypass the gate.
/// </summary>
sealed class FaultGateHttpHandler(ClientInitializer initializer) : DelegatingHandler {
	protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) {
		initializer.ThrowIfFaulted();
		return base.Send(request, cancellationToken);
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		initializer.ThrowIfFaulted();
		return base.SendAsync(request, cancellationToken);
	}
}
