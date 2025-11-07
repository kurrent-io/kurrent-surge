namespace Kurrent.Surge;

[PublicAPI]
static class ManualResetEventSlimExtensions {
    public static async ValueTask<TResult> Execute<TResult, TState>(
        this ManualResetEventSlim gatekeeper,
        Func<TState, CancellationToken, ValueTask<TResult>> execute,
        TState state, bool safe, CancellationToken token
    ) {
        try {
            gatekeeper.Wait(token);
        }
        catch (OperationCanceledException) when (safe) {
            // safe to ignore
        }

        gatekeeper.Reset();

        try {
            return await execute(state, token).ConfigureAwait(false);
        }
        finally {
            gatekeeper.Set();
        }
    }

    public static ValueTask<TResult> Execute<TResult, TState>(
        this ManualResetEventSlim gatekeeper,
        Func<TState, CancellationToken, ValueTask<TResult>> execute,
        TState state, CancellationToken token
    ) => gatekeeper.Execute(execute, state, true, token);

    public static async Task<TResult> Execute<TResult, TState>(
        this ManualResetEventSlim gatekeeper,
        Func<TState, CancellationToken, Task<TResult>> execute,
        TState state, bool safe, CancellationToken token
    ) {
        try {
            gatekeeper.Wait(token);
        }
        catch (OperationCanceledException) when (safe) {
            // safe to ignore
        }

        gatekeeper.Reset();

        try {
            return await execute(state, token).ConfigureAwait(false);
        }
        finally {
            gatekeeper.Set();
        }
    }

    public static Task<TResult> Execute<TResult, TState>(
        this ManualResetEventSlim gatekeeper,
        Func<TState, CancellationToken, Task<TResult>> execute,
        TState state, CancellationToken token
    ) => gatekeeper.Execute(execute, state, true, token);
}
