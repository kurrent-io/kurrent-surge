using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Starts catalog validation on the back of the FIRST gRPC call — the earliest moment the
/// server is provably reachable — and never awaits it, so the triggering call proceeds
/// untaxed. Single-flight: validation runs once; its task, faults included, is stored for
/// whoever awaits <see cref="Validation"/> (a governed operation, a host-start hook, a test).
/// </summary>
sealed class CatalogValidationInterceptor(Func<Task> validateCatalog) : Interceptor {
    /// <summary>
    /// The real wiring: validates the given message catalogs through governance under the
    /// resolved policy. The token ties the background validation to the client's lifetime —
    /// a disposed client must not leave a walk mid-flight against a closed channel.
    /// </summary>
    public CatalogValidationInterceptor(
        ISchemaGovernance governance,
        IEnumerable<MessageCatalog> catalogs,
        ResolvedSchemaRegistryPolicy policy,
        CancellationToken cancellationToken = default
    ) : this(() => governance.ValidateCatalog(catalogs, policy, cancellationToken)) { }

    readonly Lazy<Task> _validation = new(validateCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The single-flight validation task. First access starts it; every access returns
    /// the same task, faults included.</summary>
    public Task Validation => _validation.Value;

    void StartValidation() {
        if (_validation.IsValueCreated)
            return;

        // Fire-and-forget: a fault stays stored in the task for awaiters; touching the
        // exception here keeps a never-awaited failure off the unobserved-exception path.
        _ = _validation.Value.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) {
        StartValidation();
        return continuation(request, context);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation
    ) {
        StartValidation();
        return continuation(request, context);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        StartValidation();
        return continuation(request, context);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        StartValidation();
        return continuation(context);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context, AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        StartValidation();
        return continuation(context);
    }
}
