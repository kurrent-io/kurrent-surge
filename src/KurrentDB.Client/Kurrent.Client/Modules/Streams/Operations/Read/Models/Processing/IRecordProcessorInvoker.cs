namespace Kurrent.Client.Streams;

/// <summary>
/// Abstraction for invoking typed record processors.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables swapping invocation strategies for benchmarking and optimization.
/// Implementations bridge the gap between untyped handler resolution and typed
/// <see cref="IRecordProcessor{T}.ProcessRecord"/> invocation.
/// </para>
/// <para>
/// Available implementations:
/// <list type="bullet">
///   <item><see cref="CompiledExpressionInvoker"/> - Compiled expression trees (high performance)</item>
///   <item><see cref="ReflectionInvoker"/> - Pure reflection (baseline)</item>
///   <item><see cref="DynamicInvoker"/> - C# dynamic keyword (DLR-based)</item>
/// </list>
/// </para>
/// </remarks>
[PublicAPI]
public interface IRecordProcessorInvoker {
    /// <summary>
    /// Invokes the <see cref="IRecordProcessor{T}.ProcessRecord"/> method on the handler.
    /// </summary>
    /// <param name="handler">The handler instance (implements <see cref="IRecordProcessor{T}"/>).</param>
    /// <param name="value">The typed record value to process.</param>
    /// <param name="context">The processing context.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask InvokeAsync(object handler, object value, ProcessRecordsContext context);
}
