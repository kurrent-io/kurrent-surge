namespace Kurrent;

public static class CancellationTokenSourceExtensions {
    extension(CancellationTokenSource) {
        public static CancellationTokenSource CreateShortLived(int delay, CancellationToken ct) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(delay);
            return cts;
        }

        public static CancellationTokenSource CreateShortLived(TimeSpan delay, CancellationToken ct) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(delay);
            return cts;
        }
    }
    
    extension(CancellationTokenSource cts) {
        public CancellationToken UnsafeRegister<T>(UnsafeRegisterCallback<T> action, T state) {
            var registeredToken = cts.Token;

            registeredToken.UnsafeRegister(
                static (innerState, ct) => {
                    var (state, action) = ((UnsafeState<T>)innerState!);
                    action(state, ct).GetAwaiter().GetResult();
                }, new UnsafeState<T>(state, action));
        
            return registeredToken;
        }
    }
    
    public delegate Task UnsafeRegisterCallback<in T>(T state, CancellationToken ct);
    
    readonly record struct UnsafeState<T>(T State, UnsafeRegisterCallback<T> Action) {
        public void Deconstruct(out T state, out UnsafeRegisterCallback<T> action) {
            state  = State;
            action = Action;
        }
    }
}
