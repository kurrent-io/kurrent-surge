using System.Collections.Frozen;
using Kurrent.Client;

namespace Kurrent.Processors.Sinks;

public interface ICollector {
    /// <summary>
    /// Emit a message to all configured sinks.
    /// </summary>
    ValueTask EmitAsync<T>(T record, CancellationToken ct = default);

    /// <summary>
    /// Emit multiple messages to all configured sinks.
    /// </summary>
    async ValueTask EmitAsync<T>(IEnumerable<T> messages, CancellationToken ct = default) {
        foreach (var message in messages)
            await EmitAsync(message, ct).ConfigureAwait(false);
    }
}
/// <summary>
/// Internal runtime component that coordinates sinks.
/// <para>
/// Implements <see cref="ICollector"/> for user code, and provides
/// checkpoint coordination methods for the <c>Drone</c>.
/// </para>
/// </summary>
sealed class SinkCoordinator : ICollector {
    readonly ILogger          _logger;
    readonly FrozenSet<ISink> _sinks;

    public SinkCoordinator(IEnumerable<ISink> sinks, ILogger logger) {
        _sinks  = sinks.ToFrozenSet();
        _logger = logger;
    }

    public async ValueTask EmitAsync<T>(T message, CancellationToken ct = default) {
        await _sinks
            .Select(sink => {
                var ctx = new SinkWriteContext<T>(message) {
                    Logger            = _logger,
                    CancellationToken = ct
                };

                return sink.Write(ctx).AsTask();
            })
            .WhenAll()
            .ConfigureAwait(false);
    }

    record AggregateFlush(FrozenSet<ISink> Sinks, ILogger Logger, CancellationToken CancellationToken) : ITransaction {
        ISinkFlush[] PendingFlushes { get; set; } = [];

        public async ValueTask Complete() {
            PendingFlushes = await Sinks
                .Select(Flush)
                .WhenAll()
                .ConfigureAwait(false);

            return;

            ValueTask<ISinkFlush> Flush(ISink sink) =>
                sink.Flush(new SinkFlushContext {
                    Logger            = Logger,
                    CancellationToken = CancellationToken
                });
        }

        public async ValueTask Abort() {
            await PendingFlushes
                .Select(static flush => flush.Abort())
                .WhenAll()
                .ConfigureAwait(false);
        }
    }
}
