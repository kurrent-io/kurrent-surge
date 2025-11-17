namespace Kurrent.Surge;

static class ManualResetEventSlimExtensions {
    extension(ManualResetEventSlim gatekeeper) {
        public async ValueTask<TResult> Execute<TResult, TState>(
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

        public ValueTask<TResult> Execute<TResult, TState>(
            Func<TState, CancellationToken, ValueTask<TResult>> execute,
            TState state, CancellationToken token
        ) =>
            gatekeeper.Execute(
                execute, state, true,
                token
            );

        public async Task<TResult> Execute<TResult, TState>(
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

        public Task<TResult> Execute<TResult, TState>(
            Func<TState, CancellationToken, Task<TResult>> execute,
            TState state, CancellationToken token
        ) =>
            gatekeeper.Execute(
                execute, state, true,
                token
            );
    }
}
