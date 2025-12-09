using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Kurrent.Client.Streams;

/// <summary>
/// Invoker using compiled expression trees for high-performance invocation.
/// </summary>
/// <remarks>
/// Compiles and caches delegates per value type for optimal runtime performance.
/// Recommended for production use.
/// </remarks>
[PublicAPI]
public sealed class CompiledExpressionInvoker : IRecordProcessorInvoker {
    static readonly ConcurrentDictionary<Type, Func<object, object, ProcessRecordsContext, ValueTask>> Invokers = new();

    /// <inheritdoc />
    public ValueTask InvokeAsync(object handler, object value, ProcessRecordsContext context) {
        var invoker = Invokers.GetOrAdd(value.GetType(), CreateInvoker);
        return invoker(handler, value, context);
    }

    static Func<object, object, ProcessRecordsContext, ValueTask> CreateInvoker(Type valueType) {
        // Expression: (handler, value, ctx) => ((IRecordProcessor<T>)handler).ProcessRecord((T)value, ctx)
        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var valueParam   = Expression.Parameter(typeof(object), "value");
        var contextParam = Expression.Parameter(typeof(ProcessRecordsContext), "ctx");

        var processorType = typeof(IRecordProcessor<>).MakeGenericType(valueType);
        var method        = processorType.GetMethod(nameof(IRecordProcessor<object>.ProcessRecord))!;

        var call = Expression.Call(
            Expression.Convert(handlerParam, processorType),
            method,
            Expression.Convert(valueParam, valueType),
            contextParam
        );

        return Expression.Lambda<Func<object, object, ProcessRecordsContext, ValueTask>>(
            call,
            handlerParam,
            valueParam,
            contextParam
        ).Compile();
    }
}

/// <summary>
/// Invoker using pure reflection for baseline comparison.
/// </summary>
/// <remarks>
/// Caches <see cref="MethodInfo"/> per value type but invokes via reflection.
/// Useful for benchmarking against compiled expressions.
/// </remarks>
[PublicAPI]
public sealed class ReflectionInvoker : IRecordProcessorInvoker {
    static readonly ConcurrentDictionary<Type, MethodInfo> Methods = new();

    /// <inheritdoc />
    public ValueTask InvokeAsync(object handler, object value, ProcessRecordsContext context) {
        var method = Methods.GetOrAdd(value.GetType(), GetProcessRecordMethod);
        return (ValueTask)method.Invoke(handler, [value, context])!;
    }

    static MethodInfo GetProcessRecordMethod(Type valueType) {
        var processorType = typeof(IRecordProcessor<>).MakeGenericType(valueType);
        return processorType.GetMethod(nameof(IRecordProcessor<object>.ProcessRecord))!;
    }
}

/// <summary>
/// Invoker using C# dynamic keyword (DLR-based).
/// </summary>
/// <remarks>
/// Simple implementation using the Dynamic Language Runtime.
/// No caching required - the DLR handles call site optimization internally.
/// </remarks>
[PublicAPI]
public sealed class DynamicInvoker : IRecordProcessorInvoker {
    /// <inheritdoc />
    public ValueTask InvokeAsync(object handler, object value, ProcessRecordsContext context)
        => ((dynamic)handler).ProcessRecord((dynamic)value, context);
}
