using System.Diagnostics;

namespace Kurrent.Toolkit;

static class WithExtensions {
    extension<T>(T instance) {
        [DebuggerStepThrough]
        public T With(Action<T> apply) {
            apply(instance);
            return instance;
        }

        [DebuggerStepThrough]
        public T With(Func<T, T> apply) =>
            apply(instance);

        [DebuggerStepThrough]
        public T With<TState>(Action<T, TState> apply, TState state) {
            apply(instance, state);
            return instance;
        }

        [DebuggerStepThrough]
        public T With<TState>(Func<T, TState, T> apply, TState state) =>
            apply(instance, state);
    }
}
