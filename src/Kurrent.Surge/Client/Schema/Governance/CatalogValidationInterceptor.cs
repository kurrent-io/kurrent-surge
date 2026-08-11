using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Client.Schema.Governance;

/// <summary>
/// Starts catalog validation on the back of the FIRST gRPC call — the earliest moment the
/// server is provably reachable — and never awaits it, so the triggering call proceeds
/// untaxed. Single-flight: validation runs once, and its task carries the results — the
/// wire schema info of every validated catalog entry, faults included — for whoever awaits
/// <see cref="Validation"/>: the pipeline's govern step, a host-start hook, a test.
/// </summary>
sealed class CatalogValidationInterceptor(Func<Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>>> validateCatalog) : Interceptor {
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

    // The walk's own registry RPCs travel through the SAME invoker this interceptor is
    // mounted on. Without the shield they re-enter the Lazy on the same thread and .NET
    // throws "ValueFactory attempted to access the Value property of this instance",
    // killing walk entries under any registering/validating policy. The AsyncLocal flows
    // with the walk's async scope, so its calls pass straight through.
    static readonly AsyncLocal<bool> ValidationInFlight = new();

    readonly Lazy<Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>>> _validation =
        new(() => RunShielded(validateCatalog), LazyThreadSafetyMode.ExecutionAndPublication);

    static async Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>> RunShielded(
        Func<Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>>> validateCatalog
    ) {
        ValidationInFlight.Value = true;

        try {
            return await validateCatalog().ConfigureAwait(false);
        }
        finally {
            ValidationInFlight.Value = false;
        }
    }

    /// <summary>The single-flight validation task carrying the validated schema info. First
    /// access starts it; every access returns the same task, faults included.</summary>
    public Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>> Validation => _validation.Value;

    void StartValidation() {
        if (ValidationInFlight.Value || _validation.IsValueCreated)
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
