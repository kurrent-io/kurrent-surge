using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Dispatch map from a record's decoded message type to its handler, carrying both lifetimes in one
/// type: open for registration until the first lookup, frozen from then on. Registering after the
/// freeze throws instead of silently landing in a map dispatch no longer reads.
/// </summary>
/// <remarks>
/// Dispatch goes through <c>Resolve</c>, which owns the routing: a record's decoded type first, then
/// the fallback — which also takes records that never decoded. The result says who handles the record
/// and whether it fell back, so a dispatch site can observe fallback traffic. The caller invokes the
/// handler it gets back, so the handler's shape stays the caller's business and nothing allocates per
/// dispatch.
/// </remarks>
sealed class HandlerRegistry<THandler> where THandler : class {
    readonly Lock _lock = new();

    Dictionary<Type, THandler>? _open = [];
    THandler?                   _fallback;

    // Double-checked: the lock is only ever contended around the first dispatch. Without it, one
    // thread can null out _open while another is still freezing from it, which is a crash — not
    // merely a redundant freeze.
    FrozenDictionary<Type, THandler> Handlers {
        get {
            if (field is not null)
                return field;

            lock (_lock) {
                if (field is null) {
                    field = _open!.ToFrozenDictionary();
                    _open = null;
                }

                return field;
            }
        }
    }

    /// <summary>Registers the handler for <typeparamref name="TMessage"/>, replacing any previous one.</summary>
    public HandlerRegistry<THandler> Add<TMessage>(THandler handler) {
        lock (_lock) {
            if (_open is null)
                throw new InvalidOperationException(
                    $"Handlers are frozen once dispatch starts. Register {typeof(TMessage).Name} before the processor runs.");

            _open[typeof(TMessage)] = handler;
        }

        return this;
    }

    /// <summary>
    /// Registers the handler for records nothing else takes: message types with no registered handler,
    /// and undecoded records — <c>Resolve</c> routes both to it.
    /// </summary>
    public HandlerRegistry<THandler> Fallback(THandler handler) {
        _fallback = handler;
        return this;
    }

    public bool HasFallback => _fallback is { };

    /// <summary>
    /// Whether a handler is registered for this record's decoded message type. The fallback is not a
    /// registration and never counts here — it only participates in resolution, when this answer is no.
    /// </summary>
    public bool CanHandle(Record record) =>
        record.IsDecoded && Handlers.ContainsKey(record.ValueType);

    /// <summary>
    /// Resolves the handler for this record in one lookup — its decoded type first, then the
    /// fallback, which also takes records that never decoded. The result also says whether the
    /// record fell back, so dispatch sites can log or measure fallback traffic.
    /// </summary>
    public Match Resolve(Record record) {
        if (record.IsDecoded && Handlers.TryGetValue(record.ValueType, out var typed))
            return new() { Handler = typed };

        return new() { Handler = _fallback, IsFallback = _fallback is { } };
    }

    /// <summary>How a record's dispatch resolved: to a typed handler, to the fallback, or to nothing.</summary>
    public readonly struct Match {
        public THandler? Handler    { get; init; }
        public bool      IsFallback { get; init; }

        /// <summary>Whether anything handles the record. When true, <see cref="Handler"/> is set.</summary>
        [MemberNotNullWhen(true, nameof(Handler))]
        public bool Found => Handler is { };

        public void Deconstruct(out THandler? handler, out bool isFallback, out bool found) {
            handler    = Handler;
            isFallback = IsFallback;
            found      = Found;
        }
    }
}
