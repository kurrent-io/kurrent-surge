namespace Kurrent.Client.Streams;

/// <summary>
/// Entry point for processing KurrentDB records.
/// </summary>
/// <remarks>
/// <para>
/// Provides both fluent and direct APIs for configuring and executing record processors.
/// Use <see cref="Create(IRecordProcessor)"/> or <see cref="Create(IRecordProcessorFactory)"/>
/// for the fluent API, or the static <see cref="RunAsync"/> overloads for direct execution.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Fluent API (recommended)
/// await KurrentProcess.Create(new MyProcessor())
///     .ConnectTo("kurrentdb://localhost:2113")
///     .WithFilter(RecordFilter.ByStream("orders"))
///     .RunAsync(stoppingToken);
///
/// // Direct API
/// await KurrentProcess.RunAsync(
///     processor,
///     opts => opts with { Batching = new() { Size = 200 } },
///     client => client with { ... },
///     stoppingToken);
/// </code>
/// </example>
[PublicAPI]
public class KurrentProcess {
    /// <summary>
    /// Creates a fluent runner for the specified processor factory.
    /// </summary>
    /// <param name="processorFactory">Factory to create processor instances.</param>
    /// <returns>A <see cref="KurrentProcessRunner"/> for fluent configuration.</returns>
    /// <remarks>
    /// <para>
    /// Use a factory when you need a new processor instance for each processing operation,
    /// such as when the processor maintains state or requires dependency injection.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// await KurrentProcess.Create(new MyProcessorFactory(logger))
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithFilter(RecordFilter.ByStream("orders"))
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public static KurrentProcessRunner Create(IRecordProcessorFactory processorFactory) =>
        new(processorFactory);

    /// <summary>
    /// Creates a fluent runner for the specified processor instance.
    /// </summary>
    /// <param name="processor">The record processor to run.</param>
    /// <returns>A <see cref="KurrentProcessRunner"/> for fluent configuration.</returns>
    /// <example>
    /// <code>
    /// await KurrentProcess.Create(new MyProcessor())
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public static KurrentProcessRunner Create(IRecordProcessor processor) =>
        Create(new PassthroughRecordProcessorFactory(processor));

    /// <summary>
    /// Initializes a new instance of <see cref="KurrentProcess"/> for processing records.
    /// </summary>
    internal KurrentProcess(KurrentClient client, IRecordProcessor processor, ProcessOptions options) {
        options.EnsureValid(); // TODO SS: should use options base...

        Client    = client;
        Processor = processor;
        Options   = options;
    }

    KurrentClient    Client    { get; }
    IRecordProcessor Processor { get; }
    ProcessOptions   Options   { get; }

    internal async ValueTask RunAsync(CancellationToken cancellationToken = default) {
        // can only run once per instance? (to run multiple times, create multiple instances)
        // this might need to be enforced via a flag... not sure...

        var  startTimestamp = TimeProvider.System.GetTimestamp();
        var  lastPosition   = LogPosition.Unset;
        long totalRecords   = 0;
        var  subscriptionId = Guid.Empty;

        try {
            await HandleInitialization().ConfigureAwait(false);

            // to be optimized; for now throw the exceptions as is
            await using var stream = await Client.Streams
                .Consume(Options, cancellationToken)
                .UnwrapAsync()
                .ConfigureAwait(false);

            subscriptionId = stream.SubscriptionId;

            await foreach (var result in stream)
                await result.SwitchAsync(HandleRecords, HandleHeartbeat).ConfigureAwait(false);

            // completed successfully
            await HandleShutdown().ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleShutdown(ex).ConfigureAwait(false);
        }

        return;

        async ValueTask HandleInitialization() {
            try {
                var ctx = new InitializationContext(Options, cancellationToken);
                await Processor.Initialize(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                throw new InitializeException(ex);
            }
        }

        async ValueTask HandleRecords(Record[] records) {
            try {
                var ctx = new ProcessRecordsContext(subscriptionId, records, null, cancellationToken);
                await Processor.ProcessRecords(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                throw new ProcessRecordsException(ex);
            }

            var lastRecord = records[^1];

            lastPosition =  lastRecord.Position;
            totalRecords += records.Length;
        }

        async ValueTask HandleHeartbeat(Heartbeat heartbeat) {
            try {
                var ctx = new HeartbeatContext(subscriptionId, heartbeat, cancellationToken);
                await Processor.OnHeartbeat(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                throw new HeartbeatException(ex);
            }

            lastPosition = heartbeat.Position;
        }

        async ValueTask HandleShutdown(Exception? ex = null) {
            var uptime = TimeProvider.System.GetElapsedTime(startTimestamp);

            ShutdownContext ctx = ex switch {
                null                       => new(ShutdownReason.Completed, subscriptionId, lastPosition, totalRecords, uptime),
                OperationCanceledException => new(ShutdownReason.Cancelled, subscriptionId, lastPosition, totalRecords, uptime, ex),
                _                          => new(ShutdownReason.Error, subscriptionId, lastPosition, totalRecords, uptime, ex)
            };

            try {
                await Processor.OnShutdown(ctx).ConfigureAwait(false);
            }
            catch (Exception vex) {
                throw new ShutdownException(vex);
            }
        }
    }
}
