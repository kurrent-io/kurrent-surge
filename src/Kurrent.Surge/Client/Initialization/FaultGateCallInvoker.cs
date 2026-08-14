using Grpc.Core;

namespace Kurrent.Surge.Client;

/// <summary>
/// Enforces the initialization fault on every gRPC call: a faulted client makes no calls.
/// Decorates the client's invokers so every module gates without naming the gate.
/// </summary>
sealed class FaultGateCallInvoker(CallInvoker inner, ClientInitializer initializer) : CallInvoker {
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		initializer.ThrowIfFaulted();
		return inner.BlockingUnaryCall(method, host, options, request);
	}

	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		initializer.ThrowIfFaulted();
		return inner.AsyncUnaryCall(method, host, options, request);
	}

	public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		initializer.ThrowIfFaulted();
		return inner.AsyncServerStreamingCall(method, host, options, request);
	}

	public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options
	) {
		initializer.ThrowIfFaulted();
		return inner.AsyncClientStreamingCall(method, host, options);
	}

	public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options
	) {
		initializer.ThrowIfFaulted();
		return inner.AsyncDuplexStreamingCall(method, host, options);
	}
}
