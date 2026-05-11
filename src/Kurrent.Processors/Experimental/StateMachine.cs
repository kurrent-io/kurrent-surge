using System.Collections.Frozen;

namespace Kurrent.Processors.Experimental;

sealed class StateMachine<TState>(TState initial, FrozenSet<(TState From, TState To)> validTransitions)
    where TState : struct, Enum {
    readonly Lock _gate = new();

    TState _current = initial;

    public TState Current {
        get
        { lock (_gate)
              return _current; }
    }

    /// <summary>
    /// Atomically transitions if the move is valid AND the current state matches an
    /// expected gate value. Returns true on success. Use this when racing callers
    /// must serialize on the transition itself (e.g., only the first caller drains).
    /// </summary>
    public bool TryTransition(TState to) {
        lock (_gate) {
            if (!validTransitions.Contains((_current, to)))
                return false;

            _current = to;
            return true;
        }
    }

    public void Transition(TState to) {
        lock (_gate) {
            if (!validTransitions.Contains((_current, to)))
                throw new InvalidOperationException($"Invalid transition: {_current} → {to}");

            _current = to;
        }
    }
}