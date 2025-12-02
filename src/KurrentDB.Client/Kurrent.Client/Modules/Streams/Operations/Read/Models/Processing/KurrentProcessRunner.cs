namespace Kurrent.Client.Streams;

/// <summary>
/// Fluent runner for configuring and executing a KurrentDB record processor.
/// </summary>
/// <remarks>
/// <para>
/// Provides a fluent API for configuring processing options, client options, and
/// processor instances before executing the processing operation with <see cref="RunAsync"/>.
/// </para>
/// <para>
/// Start with <see cref="KurrentProcess.Create(IRecordProcessor)"/> or
/// <see cref="KurrentProcess.Create(IRecordProcessorFactory)"/> to create a runner instance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Minimal usage with processor instance
/// await KurrentProcess.Create(new MyProcessor()).RunAsync(stoppingToken);
///
/// // With processor factory and a provided client
/// await KurrentProcess.Create(processorFactory)
///     .UseClient(existingClient)
///     .WithFilter(RecordFilter.ByStream("orders"))
///     .RunAsync(stoppingToken);
///
/// // With processor factory and full configuration
/// await KurrentProcess.Create(processorFactory)
///     .WithClient("kurrentdb://admin:changeit@localhost:2113?tls=true", opts => opts with {
///         Resilience = KurrentClientResilienceOptions.HighAvailability
///     })
///     .WithFilter(RecordFilter.ByStream("orders"))
///     .WithInitialPosition(InitialPosition.Earliest)
///     .WithBatching(new BatchingOptions { Size = 200, Timeout = 1000 })
///     .WithHeartbeat(HeartbeatOptions.Threshold(5000))
///     .WithBufferCapacity(5000)
///     .RunAsync(stoppingToken);
/// </code>
/// </example>
[PublicAPI]
public sealed class KurrentProcessRunner {
    internal KurrentProcessRunner(IRecordProcessorFactory processorFactory) {
        GetProcessor = () => {
            try {
                return processorFactory.CreateProcessor();
            }
            catch (Exception ex) {
                throw new ProcessorCreationException(ex); // is this the best name? what about names like ProcessorInstantiationException etc.
            }
        };

        ProcessOptions   = new ProcessOptions();
        GetClientOptions = () => new KurrentClientOptions();
    }

    Func<IRecordProcessor>     GetProcessor     { get; set; }
    ProcessOptions             ProcessOptions   { get; set; }
    KurrentClient?             ProvidedClient   { get; set; }
    Func<KurrentClientOptions> GetClientOptions { get; set; }

    /// <summary>
    /// Sets the connection string and configures client options in a single call.
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113" or "kurrentdb://admin:changeit@localhost:2113?tls=true").
    /// </param>
    /// <param name="configure">Function to configure client options derived from the connection string.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// The configure function receives the options parsed from the connection string, allowing
    /// modifications to the connection-derived settings.
    /// </para>
    /// <para>
    /// This method invalidates any previously provided client instance.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithClient("kurrentdb://localhost:2113", opts => opts with {
    ///     Resilience = KurrentClientResilienceOptions.HighAvailability
    /// })
    /// </code>
    /// </example>
    public KurrentProcessRunner WithClient(string connectionString, Func<KurrentClientOptions, KurrentClientOptions> configure) {
        GetClientOptions = () => configure(KurrentClientOptions.Parse(connectionString));
        ProvidedClient   = null;
        return this;
    }

    /// <summary>
    /// Sets pre-configured client options for connecting to KurrentDB.
    /// </summary>
    /// <param name="options">The pre-configured client options.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method invalidates any previously provided client instance.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner WithClient(KurrentClientOptions options) {
        GetClientOptions = () => options;
        ProvidedClient   = null;
        return this;
    }

    /// <summary>
    /// Uses an existing KurrentClient instance for processing.
    /// </summary>
    /// <param name="client">The client instance to use.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When using a provided client instance, the runner will NOT dispose it after processing completes.
    /// The caller is responsible for the client's lifecycle.
    /// </para>
    /// <para>
    /// This method invalidates any previously configured client options via <see cref="ConnectTo"/> or
    /// <see cref="WithClient(string, Func{KurrentClientOptions, KurrentClientOptions})"/>.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner UseClient(KurrentClient client) {
        GetClientOptions = () => client.Options;
        ProvidedClient   = client;
        return this;
    }

    /// <summary>
    /// Sets the connection string for connecting to KurrentDB.
    /// <remarks>
    /// <para>
    /// Any previously set client options will be overridden, and any provided client
    /// instance will be ignored.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="connectionString">
    /// KurrentDB connection string (e.g., "kurrentdb://localhost:2113" or "kurrentdb://admin:changeit@localhost:2113?tls=true").
    /// </param>
    /// <returns>The runner instance for method chaining.</returns>
    public KurrentProcessRunner ConnectTo(string connectionString) => WithClient(connectionString, options => options);

    /// <summary>
    /// Sets the record filter for processing.
    /// </summary>
    /// <param name="filter">The filter to apply to records.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Filters reduce data transfer and processing overhead by targeting specific streams,
    /// schemas, or metadata properties.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithFilter(RecordFilter.ByStream("orders"))
    /// .WithFilter(RecordFilter.ByStreamPrefix("tenant-"))
    /// .WithFilter(RecordFilter.BySchema("OrderPlaced"))
    /// </code>
    /// </example>
    public KurrentProcessRunner WithFilter(IRecordFilter filter) {
        ProcessOptions = ProcessOptions with { Filter = filter };
        return this;
    }

    /// <summary>
    /// Sets the initial position for processing when no checkpoint exists.
    /// </summary>
    /// <param name="position">The initial position (Earliest or Latest).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This setting only applies when there is no stored checkpoint.
    /// Use <see cref="WithStartPosition"/> to override any existing checkpoint.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner WithInitialPosition(InitialPosition position) {
        ProcessOptions = ProcessOptions with { InitialPosition = position };
        return this;
    }

    /// <summary>
    /// Forces processing to start from a specific position, ignoring any checkpoint.
    /// </summary>
    /// <param name="position">
    /// The position to start from. Can be either a <see cref="LogPosition"/> for global log positioning
    /// or a <see cref="StreamRevision"/> for stream-specific positioning.
    /// </param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When set, any existing checkpoint will be ignored and processing will begin from
    /// the specified position.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Start from a specific log position
    /// .WithStartPosition(LogPosition.Start)
    ///
    /// // Start from a specific stream revision
    /// .WithStartPosition(StreamRevision.From(100))
    /// </code>
    /// </example>
    public KurrentProcessRunner WithStartPosition(StartPosition position) {
        ProcessOptions = ProcessOptions with { StartPosition = position };
        return this;
    }

    /// <summary>
    /// Configures record batching for processing.
    /// </summary>
    /// <param name="options">The batching configuration.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Batching controls how records are grouped before delivery to the processor.
    /// Smaller batches reduce latency; larger batches improve throughput.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithBatching(new BatchingOptions { Size = 200, Timeout = 1000 })
    /// </code>
    /// </example>
    public KurrentProcessRunner WithBatching(BatchingOptions options) {
        ProcessOptions = ProcessOptions with { Batching = options };
        return this;
    }

    /// <summary>
    /// Configures heartbeat options for progress feedback during processing.
    /// </summary>
    /// <param name="options">The heartbeat configuration.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Heartbeats provide position updates and indicate the stream is alive.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithHeartbeat(HeartbeatOptions.Threshold(5000))
    /// </code>
    /// </example>
    public KurrentProcessRunner WithHeartbeat(HeartbeatOptions options) {
        ProcessOptions = ProcessOptions with { Heartbeat = options };
        return this;
    }

    /// <summary>
    /// Sets the buffer capacity for record buffering.
    /// </summary>
    /// <param name="capacity">Buffer capacity in records (default: 1000, max: 10000).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Larger buffers improve throughput but increase memory usage.
    /// The buffer capacity should be at least as large as the batch size.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner WithBufferCapacity(int capacity) {
        ProcessOptions = ProcessOptions with { BufferCapacity = capacity };
        return this;
    }

    /// <summary>
    /// Skips record deserialization for raw byte access.
    /// </summary>
    /// <param name="skip">True to skip decoding (default: true when called).</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use when processing metadata only or with custom deserialization.
    /// Reduces CPU usage in high-throughput scenarios.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner WithSkipDecoding(bool skip = true) {
        ProcessOptions = ProcessOptions with { SkipDecoding = skip };
        return this;
    }

    /// <summary>
    /// Provides full control over process options.
    /// </summary>
    /// <param name="configure">Function to configure process options.</param>
    /// <returns>The runner instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Use this method for advanced processing configuration that isn't covered
    /// by the individual property methods.
    /// </para>
    /// <para>
    /// Any previously set processing options will be overridden.
    /// </para>
    /// </remarks>
    public KurrentProcessRunner ConfigureProcessing(Func<ProcessOptions, ProcessOptions> configure) {
        ProcessOptions = configure(ProcessOptions);
        return this;
    }

    // /// <summary>
    // /// Validates configuration and starts processing records.
    // /// </summary>
    // /// <param name="stoppingToken">Token to stop processing gracefully.</param>
    // /// <remarks>
    // /// <para>
    // /// This method validates all configuration, creates the processor, and starts the processing loop.
    // /// Processing continues until the stream ends, cancellation is requested, or an error occurs.
    // /// </para>
    // /// </remarks>
    // public async ValueTask RunAsync(CancellationToken stoppingToken = default) {
    //     ProcessOptions.EnsureValid();
    //
    //     IRecordProcessor processor;
    //     try {
    //         processor = ProcessorFactory.CreateProcessor();
    //     }
    //     catch (Exception ex) {
    //         throw new ProcessorCreationException(ex);
    //     }
    //
    //     KurrentClient? ownedClient = null;
    //     try {
    //         var client = ProvidedClient ?? (ownedClient = KurrentClient.Create(GetClientOptions()));
    //
    //         var startTimestamp = TimeProvider.System.GetTimestamp();
    //         var lastPosition   = LogPosition.Unset;
    //         long totalRecords  = 0;
    //         var subscriptionId = Guid.Empty;
    //
    //         try {
    //             await HandleInitialization().ConfigureAwait(false);
    //
    //             await using var stream = await client.Streams
    //                 .Consume(ProcessOptions, stoppingToken)
    //                 .UnwrapAsync()
    //                 .ConfigureAwait(false);
    //
    //             subscriptionId = stream.SubscriptionId;
    //
    //             await foreach (var result in stream)
    //                 await result.SwitchAsync(HandleRecords, HandleHeartbeat).ConfigureAwait(false);
    //
    //             await HandleShutdown().ConfigureAwait(false);
    //         }
    //         catch (Exception ex) {
    //             await HandleShutdown(ex).ConfigureAwait(false);
    //         }
    //
    //         return;
    //
    //         async ValueTask HandleInitialization() {
    //             try {
    //                 var ctx = new InitializationContext(ProcessOptions, stoppingToken);
    //                 await processor.Initialize(ctx).ConfigureAwait(false);
    //             }
    //             catch (Exception ex) when (ex is not OperationCanceledException) {
    //                 throw new InitializeException(ex);
    //             }
    //         }
    //
    //         async ValueTask HandleRecords(Record[] records) {
    //             try {
    //                 var ctx = new ProcessRecordsContext(subscriptionId, records, null, stoppingToken);
    //                 await processor.ProcessRecords(ctx).ConfigureAwait(false);
    //             }
    //             catch (Exception ex) when (ex is not OperationCanceledException) {
    //                 throw new ProcessRecordsException(ex);
    //             }
    //
    //             var lastRecord = records[^1];
    //
    //             lastPosition =  lastRecord.Position;
    //             totalRecords += records.Length;
    //         }
    //
    //         async ValueTask HandleHeartbeat(Heartbeat heartbeat) {
    //             try {
    //                 var ctx = new HeartbeatContext(subscriptionId, heartbeat, stoppingToken);
    //                 await processor.OnHeartbeat(ctx).ConfigureAwait(false);
    //             }
    //             catch (Exception ex) when (ex is not OperationCanceledException) {
    //                 throw new HeartbeatException(ex);
    //             }
    //
    //             lastPosition = heartbeat.Position;
    //         }
    //
    //         async ValueTask HandleShutdown(Exception? ex = null) {
    //             var uptime = TimeProvider.System.GetElapsedTime(startTimestamp);
    //
    //             ShutdownContext ctx = ex switch {
    //                 null                       => new(ShutdownReason.Completed, subscriptionId, lastPosition, totalRecords, uptime),
    //                 OperationCanceledException => new(ShutdownReason.Cancelled, subscriptionId, lastPosition, totalRecords, uptime, ex),
    //                 _                          => new(ShutdownReason.Error, subscriptionId, lastPosition, totalRecords, uptime, ex)
    //             };
    //
    //             try {
    //                 await processor.OnShutdown(ctx).ConfigureAwait(false);
    //             }
    //             catch (Exception vex) {
    //                 throw new ShutdownException(vex);
    //             }
    //         }
    //     }
    //     finally {
    //         if (ownedClient is not null)
    //             await ownedClient.DisposeAsync().ConfigureAwait(false);
    //     }
    // }

    public KurrentProcess Create() =>
        new(ProvidedClient ?? new KurrentClient(GetClientOptions()), GetProcessor(), ProcessOptions);

    public async ValueTask RunAsync() {
        var shouldDispose = ProvidedClient is null || !ProvidedClient.Shared;

        var client  = ProvidedClient ?? new KurrentClient(GetClientOptions());
        var process = new KurrentProcess(client, GetProcessor(), ProcessOptions);

        try {
            await process.RunAsync().ConfigureAwait(false);
        }
        finally {
            if (shouldDispose)
                await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
