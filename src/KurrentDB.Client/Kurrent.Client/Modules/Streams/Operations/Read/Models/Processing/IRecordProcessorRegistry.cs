namespace Kurrent.Client.Streams;

/// <summary>
/// Unified handler registration, resolution, and invocation for typed record processors.
/// </summary>
/// <remarks>
/// <para>
/// Implementations handle the complete dispatch flow:
/// <list type="number">
///   <item>Record decoding (if not already decoded)</item>
///   <item>Handler resolution by <see cref="Record.ValueType"/></item>
///   <item>Handler invocation via internal invoker strategy</item>
///   <item>Unhandled record notification</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var registry = new ServiceProviderRegistry();
/// registry.Register&lt;OrderPlaced, OrderPlacedProcessor&gt;();
/// registry.Register&lt;OrderShipped&gt;((value, ctx) =&gt; {
///     Console.WriteLine($"Shipped: {value.OrderId}");
///     return ValueTask.CompletedTask;
/// });
///
/// var processor = new DispatchingRecordProcessor(registry);
/// </code>
/// </example>
[PublicAPI]
public interface IRecordProcessorRegistry {
    /// <summary>
    /// Registers a handler type for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    void Register<T, THandler>()
        where T : class
        where THandler : class, IRecordProcessor<T>;

    /// <summary>
    /// Registers a handler instance for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="handler">The processor instance.</param>
    void Register<T>(IRecordProcessor<T> handler) where T : class;

    /// <summary>
    /// Registers a delegate handler for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="handler">The async delegate to invoke for each record.</param>
    void Register<T>(Func<T, ProcessRecordsContext, ValueTask> handler) where T : class;

    /// <summary>
    /// Registers a synchronous delegate handler for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="handler">The delegate to invoke for each record.</param>
    void Register<T>(Action<T, ProcessRecordsContext> handler) where T : class;

    /// <summary>
    /// Processes the record at the specified index in the context's record batch.
    /// </summary>
    /// <param name="recordIndex">Index of the record in <see cref="ProcessRecordsContext.Records"/>.</param>
    /// <param name="context">The processing context containing the records batch.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method handles the complete dispatch flow:
    /// <list type="number">
    ///   <item>Access record at <paramref name="recordIndex"/></item>
    ///   <item>Decode if not already decoded</item>
    ///   <item>Skip if value is null</item>
    ///   <item>Resolve handler by <see cref="Record.ValueType"/></item>
    ///   <item>Invoke handler or notify unhandled</item>
    /// </list>
    /// </para>
    /// </remarks>
    ValueTask InvokeAsync(int recordIndex, ProcessRecordsContext context);

    /// <summary>
    /// Returns all registered handlers for lifecycle method forwarding.
    /// </summary>
    /// <returns>An enumerable of all registered <see cref="IRecordProcessor"/> instances.</returns>
    IEnumerable<IRecordProcessor> GetAllHandlers();
}
