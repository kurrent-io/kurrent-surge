// ReSharper disable CheckNamespace

namespace EventSourcing;

[PublicAPI]
static class PipeExtensions {
    extension(Task first) {
        #region ->> Asynchronous Then Extensions <<-

        /// <summary>Chains an asynchronous continuation after completion.</summary>
        public async Task Then(Func<Task> next) {
            await first.ConfigureAwait(false);
            await next().ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous continuation with captured state after completion, avoiding a closure allocation.</summary>
        public async Task Then<TState>(Func<TState, Task> next, TState state) {
            await first.ConfigureAwait(false);
            await next(state).ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous continuation that produces a result after completion.</summary>
        public async Task<T> Then<T>(Func<Task<T>> next) {
            await first.ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous continuation with captured state that produces a result after completion, avoiding a closure allocation.</summary>
        public async Task<T> Then<TState, T>(Func<TState, Task<T>> next, TState state) {
            await first.ConfigureAwait(false);
            return await next(state).ConfigureAwait(false);
        }

        #endregion // Asynchronous Then Extensions

        #region ->> Synchronous Then Extensions <<-

        /// <summary>Chains a synchronous continuation after completion.</summary>
        public async Task Then(Action next) {
            await first.ConfigureAwait(false);
            next();
        }

        /// <summary>Chains a synchronous continuation with a captured argument after completion, avoiding a closure allocation.</summary>
        public async Task Then<T>(Action<T> next, T arg) {
            await first.ConfigureAwait(false);
            next(arg);
        }

        /// <summary>Chains a synchronous continuation that produces a result after completion.</summary>
        public async Task<TResult> Then<TResult>(Func<TResult> next) {
            await first.ConfigureAwait(false);
            return next();
        }

        /// <summary>Chains a synchronous continuation with captured state that produces a result after completion, avoiding a closure allocation.</summary>
        public async Task<TResult> Then<TState, TResult>(Func<TState, TResult> next, TState state) {
            await first.ConfigureAwait(false);
            return next(state);
        }

        #endregion // Synchronous Then Extensions

        #region ->> Asynchronous OnError Extensions <<-

        /// <summary>Executes an asynchronous error handler on failure.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public async Task OnError(Func<Exception, Task> errorHandler) {
            try {
                await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                await errorHandler(ex).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state on failure, avoiding a closure allocation.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public async Task OnError<TState>(Func<Exception, TState, Task> errorHandler, TState state) {
            try {
                await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                await errorHandler(ex, state).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler when a specific exception type occurs.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public async Task OnError<TException>(Func<TException, Task> errorHandler) where TException : Exception {
            try {
                await first.ConfigureAwait(false);
            }
            catch (TException ex) {
                await errorHandler(ex).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state when a specific exception type occurs, avoiding a closure allocation.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled. If the handler completes normally, the exception is swallowed.</remarks>
        public async Task OnError<TException, TState>(Func<TException, TState, Task> errorHandler, TState state) where TException : Exception {
            try {
                await first.ConfigureAwait(false);
            }
            catch (TException ex) {
                await errorHandler(ex, state).ConfigureAwait(false);
            }
        }

        #endregion // Asynchronous OnError Extensions

        #region ->> Synchronous OnError Extensions <<-

        /// <summary>Executes a synchronous error handler on failure.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public async Task OnError(Action<Exception> errorHandler) {
            try {
                await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                errorHandler(ex);
            }
        }

        /// <summary>Executes a synchronous error handler with captured state on failure, avoiding a closure allocation.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public async Task OnError<TState>(Action<Exception, TState> errorHandler, TState state) {
            try {
                await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                errorHandler(ex, state);
            }
        }

        #endregion // Synchronous OnError Extensions

        #region ->> Finally Extensions <<-

        /// <summary>Executes a synchronous action regardless of success or failure.</summary>
        public async Task Finally(Action finalAction) {
            try {
                await first.ConfigureAwait(false);
            }
            finally {
                finalAction();
            }
        }

        /// <summary>Executes a synchronous action with captured state regardless of success or failure, avoiding a closure allocation.</summary>
        public async Task Finally<TState>(Action<TState> finalAction, TState state) {
            try {
                await first.ConfigureAwait(false);
            }
            finally {
                finalAction(state);
            }
        }

        /// <summary>Executes an asynchronous action regardless of success or failure.</summary>
        public async Task Finally(Func<Task> finalAction) {
            try {
                await first.ConfigureAwait(false);
            }
            finally {
                await finalAction().ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous action with captured state regardless of success or failure, avoiding a closure allocation.</summary>
        public async Task Finally<TState>(Func<TState, Task> finalAction, TState state) {
            try {
                await first.ConfigureAwait(false);
            }
            finally {
                await finalAction(state).ConfigureAwait(false);
            }
        }

        #endregion // Finally Extensions
    }

    extension<T>(Task<T> first) {
        #region ->> Asynchronous Then Extensions <<-

        /// <summary>Chains an asynchronous continuation that transforms the result.</summary>
        public async Task<T2> Then<T2>(Func<T, Task<T2>> next) {
            var result = await first.ConfigureAwait(false);
            return await next(result).ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous continuation with captured state that transforms the result, avoiding a closure allocation.</summary>
        public async Task<T2> Then<TState, T2>(Func<T, TState, Task<T2>> next, TState state) {
            var result = await first.ConfigureAwait(false);
            return await next(result, state).ConfigureAwait(false);
        }

        /// <summary>Chains an asynchronous side-effect, preserving the result.</summary>
        public async Task<T> Then(Func<T, Task> next) {
            var result = await first.ConfigureAwait(false);
            await next(result).ConfigureAwait(false);
            return result;
        }

        /// <summary>Chains an asynchronous side-effect with captured state, preserving the result and avoiding a closure allocation.</summary>
        public async Task<T> Then<TState>(Func<T, TState, Task> next, TState state) {
            var result = await first.ConfigureAwait(false);
            await next(result, state).ConfigureAwait(false);
            return result;
        }

        #endregion // Asynchronous Then Extensions

        #region ->> Synchronous Then Extensions <<-

        /// <summary>Chains a synchronous side-effect, preserving the result.</summary>
        public async Task<T> Then(Action<T> next) {
            var result = await first.ConfigureAwait(false);
            next(result);
            return result;
        }

        /// <summary>Chains a synchronous side-effect with captured state, preserving the result and avoiding a closure allocation.</summary>
        public async Task<T> Then<TState>(Action<T, TState> next, TState state) {
            var result = await first.ConfigureAwait(false);
            next(result, state);
            return result;
        }

        /// <summary>Chains a synchronous continuation that transforms the result.</summary>
        public async Task<TResult> Then<TResult>(Func<T, TResult> next) {
            var result = await first.ConfigureAwait(false);
            return next(result);
        }

        /// <summary>Chains a synchronous continuation with captured state that transforms the result, avoiding a closure allocation.</summary>
        public async Task<TResult> Then<TState, TResult>(Func<T, TState, TResult> next, TState state) {
            var result = await first.ConfigureAwait(false);
            return next(result, state);
        }

        #endregion // Synchronous Then Extensions

        #region ->> Asynchronous OnError Extensions <<-

        /// <summary>Executes an asynchronous error handler on failure, returning a fallback result.</summary>
        public async Task<T> OnError(Func<Exception, Task<T>> errorHandler) {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                return await errorHandler(ex).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state on failure, returning a fallback result and avoiding a closure allocation.</summary>
        public async Task<T> OnError<TState>(Func<Exception, TState, Task<T>> errorHandler, TState state) {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                return await errorHandler(ex, state).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler when a specific exception type occurs, returning a fallback result.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public async Task<T> OnError<TException>(Func<TException, Task<T>> errorHandler) where TException : Exception {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (TException ex) {
                return await errorHandler(ex).ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state when a specific exception type occurs, returning a fallback result and avoiding a closure allocation.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public async Task<T> OnError<TException, TState>(Func<TException, TState, Task<T>> errorHandler, TState state) where TException : Exception {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (TException ex) {
                return await errorHandler(ex, state).ConfigureAwait(false);
            }
        }

        #endregion // Asynchronous OnError Extensions

        #region ->> Synchronous OnError Extensions <<-

        /// <summary>Executes a synchronous error handler on failure, returning a fallback result.</summary>
        public async Task<T> OnError(Func<Exception, T> errorHandler) {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                return errorHandler(ex);
            }
        }

        /// <summary>Executes a synchronous error handler with captured state on failure, returning a fallback result and avoiding a closure allocation.</summary>
        public async Task<T> OnError<TState>(Func<Exception, TState, T> errorHandler, TState state) {
            try {
                return await first.ConfigureAwait(false);
            }
            catch (Exception ex) {
                return errorHandler(ex, state);
            }
        }

        #endregion // Synchronous OnError Extensions

        #region ->> Finally Extensions <<-

        /// <summary>Executes a synchronous action regardless of success or failure, preserving the result.</summary>
        public async Task<T> Finally(Action finalAction) {
            try {
                return await first.ConfigureAwait(false);
            }
            finally {
                finalAction();
            }
        }

        /// <summary>Executes a synchronous action with captured state regardless of success or failure, preserving the result and avoiding a closure allocation.</summary>
        public async Task<T> Finally<TState>(Action<TState> finalAction, TState state) {
            try {
                return await first.ConfigureAwait(false);
            }
            finally {
                finalAction(state);
            }
        }

        /// <summary>Executes an asynchronous action regardless of success or failure, preserving the result.</summary>
        public async Task<T> Finally(Func<Task> finalAction) {
            try {
                return await first.ConfigureAwait(false);
            }
            finally {
                await finalAction().ConfigureAwait(false);
            }
        }

        /// <summary>Executes an asynchronous action with captured state regardless of success or failure, preserving the result and avoiding a closure allocation.</summary>
        public async Task<T> Finally<TState>(Func<TState, Task> finalAction, TState state) {
            try {
                return await first.ConfigureAwait(false);
            }
            finally {
                await finalAction(state).ConfigureAwait(false);
            }
        }

        #endregion // Finally Extensions
    }

    extension(ValueTask first) {
        #region ->> Asynchronous Then Extensions <<-

        /// <summary>Chains an asynchronous continuation after completion.</summary>
        public ValueTask Then(Func<ValueTask> next) {
            if (first.IsCompletedSuccessfully) return next();
            return WhenFirstPending(first, next);

            static async ValueTask WhenFirstPending(ValueTask first, Func<ValueTask> next) {
                await first.ConfigureAwait(false);
                await next().ConfigureAwait(false);
            }
        }

        /// <summary>Chains an asynchronous continuation with captured state after completion, avoiding a closure allocation.</summary>
        public ValueTask Then<TState>(Func<TState, ValueTask> next, TState state) {
            if (first.IsCompletedSuccessfully) return next(state);
            return WhenFirstPending(first, next, state);

            static async ValueTask WhenFirstPending(ValueTask first, Func<TState, ValueTask> next, TState state) {
                await first.ConfigureAwait(false);
                await next(state).ConfigureAwait(false);
            }
        }

        /// <summary>Chains an asynchronous continuation that produces a result after completion.</summary>
        public ValueTask<T> Then<T>(Func<ValueTask<T>> next) {
            if (first.IsCompletedSuccessfully) return next();
            return WhenFirstPending(first, next);

            static async ValueTask<T> WhenFirstPending(ValueTask first, Func<ValueTask<T>> next) {
                await first.ConfigureAwait(false);
                return await next().ConfigureAwait(false);
            }
        }

        /// <summary>Chains an asynchronous continuation with captured state that produces a result after completion, avoiding a closure allocation.</summary>
        public ValueTask<T> Then<TState, T>(Func<TState, ValueTask<T>> next, TState state) {
            if (first.IsCompletedSuccessfully) return next(state);
            return WhenFirstPending(first, next, state);

            static async ValueTask<T> WhenFirstPending(ValueTask first, Func<TState, ValueTask<T>> next, TState state) {
                await first.ConfigureAwait(false);
                return await next(state).ConfigureAwait(false);
            }
        }

        #endregion // Asynchronous Then Extensions

        #region ->> Synchronous Then Extensions <<-

        /// <summary>Chains a synchronous continuation after completion.</summary>
        public ValueTask Then(Action next) {
            if (first.IsCompletedSuccessfully) { next(); return default; }
            return WhenFirstPending(first, next);

            static async ValueTask WhenFirstPending(ValueTask first, Action next) {
                await first.ConfigureAwait(false);
                next();
            }
        }

        /// <summary>Chains a synchronous continuation with a captured argument after completion, avoiding a closure allocation.</summary>
        public ValueTask Then<T>(Action<T> next, T arg) {
            if (first.IsCompletedSuccessfully) { next(arg); return default; }
            return WhenFirstPending(first, next, arg);

            static async ValueTask WhenFirstPending(ValueTask first, Action<T> next, T arg) {
                await first.ConfigureAwait(false);
                next(arg);
            }
        }

        /// <summary>Chains a synchronous continuation that produces a result after completion.</summary>
        public ValueTask<TResult> Then<TResult>(Func<TResult> next) {
            if (first.IsCompletedSuccessfully) return new ValueTask<TResult>(next());
            return WhenFirstPending(first, next);

            static async ValueTask<TResult> WhenFirstPending(ValueTask first, Func<TResult> next) {
                await first.ConfigureAwait(false);
                return next();
            }
        }

        /// <summary>Chains a synchronous continuation with captured state that produces a result after completion, avoiding a closure allocation.</summary>
        public ValueTask<TResult> Then<TState, TResult>(Func<TState, TResult> next, TState state) {
            if (first.IsCompletedSuccessfully) return new ValueTask<TResult>(next(state));
            return WhenFirstPending(first, next, state);

            static async ValueTask<TResult> WhenFirstPending(ValueTask first, Func<TState, TResult> next, TState state) {
                await first.ConfigureAwait(false);
                return next(state);
            }
        }

        #endregion // Synchronous Then Extensions

        #region ->> Asynchronous OnError Extensions <<-

        /// <summary>Executes an asynchronous error handler on failure.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public ValueTask OnError(Func<Exception, ValueTask> errorHandler) {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask WhenFirstPending(ValueTask first, Func<Exception, ValueTask> errorHandler) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    await errorHandler(ex).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state on failure, avoiding a closure allocation.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public ValueTask OnError<TState>(Func<Exception, TState, ValueTask> errorHandler, TState state) {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask WhenFirstPending(ValueTask first, Func<Exception, TState, ValueTask> errorHandler, TState state) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    await errorHandler(ex, state).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler when a specific exception type occurs.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public ValueTask OnError<TException>(Func<TException, ValueTask> errorHandler) where TException : Exception {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask WhenFirstPending(ValueTask first, Func<TException, ValueTask> errorHandler) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (TException ex) {
                    await errorHandler(ex).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state when a specific exception type occurs, avoiding a closure allocation.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled. If the handler completes normally, the exception is swallowed.</remarks>
        public ValueTask OnError<TException, TState>(Func<TException, TState, ValueTask> errorHandler, TState state) where TException : Exception {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask WhenFirstPending(ValueTask first, Func<TException, TState, ValueTask> errorHandler, TState state) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (TException ex) {
                    await errorHandler(ex, state).ConfigureAwait(false);
                }
            }
        }

        #endregion // Asynchronous OnError Extensions

        #region ->> Synchronous OnError Extensions <<-

        /// <summary>Executes a synchronous error handler on failure.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public ValueTask OnError(Action<Exception> errorHandler) {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask WhenFirstPending(ValueTask first, Action<Exception> errorHandler) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    errorHandler(ex);
                }
            }
        }

        /// <summary>Executes a synchronous error handler with captured state on failure, avoiding a closure allocation.</summary>
        /// <remarks>If the handler completes normally, the exception is swallowed.</remarks>
        public ValueTask OnError<TState>(Action<Exception, TState> errorHandler, TState state) {
            if (first.IsCompletedSuccessfully) return default;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask WhenFirstPending(ValueTask first, Action<Exception, TState> errorHandler, TState state) {
                try {
                    await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    errorHandler(ex, state);
                }
            }
        }

        #endregion // Synchronous OnError Extensions

        #region ->> Finally Extensions <<-

        /// <summary>Executes a synchronous action regardless of success or failure.</summary>
        public ValueTask Finally(Action finalAction) {
            if (first.IsCompletedSuccessfully) { finalAction(); return default; }
            return WhenFirstPending(first, finalAction);

            static async ValueTask WhenFirstPending(ValueTask first, Action finalAction) {
                try {
                    await first.ConfigureAwait(false);
                }
                finally {
                    finalAction();
                }
            }
        }

        /// <summary>Executes a synchronous action with captured state regardless of success or failure, avoiding a closure allocation.</summary>
        public ValueTask Finally<TState>(Action<TState> finalAction, TState state) {
            if (first.IsCompletedSuccessfully) { finalAction(state); return default; }
            return WhenFirstPending(first, finalAction, state);

            static async ValueTask WhenFirstPending(ValueTask first, Action<TState> finalAction, TState state) {
                try {
                    await first.ConfigureAwait(false);
                }
                finally {
                    finalAction(state);
                }
            }
        }

        /// <summary>Executes an asynchronous action regardless of success or failure.</summary>
        public ValueTask Finally(Func<ValueTask> finalAction) {
            if (first.IsCompletedSuccessfully) return finalAction();
            return WhenFirstPending(first, finalAction);

            static async ValueTask WhenFirstPending(ValueTask first, Func<ValueTask> finalAction) {
                try {
                    await first.ConfigureAwait(false);
                }
                finally {
                    await finalAction().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous action with captured state regardless of success or failure, avoiding a closure allocation.</summary>
        public ValueTask Finally<TState>(Func<TState, ValueTask> finalAction, TState state) {
            if (first.IsCompletedSuccessfully) return finalAction(state);
            return WhenFirstPending(first, finalAction, state);

            static async ValueTask WhenFirstPending(ValueTask first, Func<TState, ValueTask> finalAction, TState state) {
                try {
                    await first.ConfigureAwait(false);
                }
                finally {
                    await finalAction(state).ConfigureAwait(false);
                }
            }
        }

        #endregion // Finally Extensions
    }

    extension<T>(ValueTask<T> first) {
        #region ->> Asynchronous Then Extensions <<-

        /// <summary>Chains an asynchronous continuation that transforms the result.</summary>
        public ValueTask<T2> Then<T2>(Func<T, ValueTask<T2>> next) {
            if (first.IsCompletedSuccessfully) return next(first.Result);
            return WhenFirstPending(first, next);

            static async ValueTask<T2> WhenFirstPending(ValueTask<T> first, Func<T, ValueTask<T2>> next) {
                var result = await first.ConfigureAwait(false);
                return await next(result).ConfigureAwait(false);
            }
        }

        /// <summary>Chains an asynchronous continuation with captured state that transforms the result, avoiding a closure allocation.</summary>
        public ValueTask<T2> Then<TState, T2>(Func<T, TState, ValueTask<T2>> next, TState state) {
            if (first.IsCompletedSuccessfully) return next(first.Result, state);
            return WhenFirstPending(first, next, state);

            static async ValueTask<T2> WhenFirstPending(ValueTask<T> first, Func<T, TState, ValueTask<T2>> next, TState state) {
                var result = await first.ConfigureAwait(false);
                return await next(result, state).ConfigureAwait(false);
            }
        }

        /// <summary>Chains an asynchronous side-effect, preserving the result.</summary>
        public ValueTask<T> Then(Func<T, ValueTask> next) {
            if (first.IsCompletedSuccessfully) {
                var result = first.Result;
                var sideEffect = next(result);
                if (sideEffect.IsCompletedSuccessfully) return new ValueTask<T>(result);
                return WhenSideEffectPending(result, sideEffect);
            }
            return WhenFirstPending(first, next);

            static async ValueTask<T> WhenSideEffectPending(T result, ValueTask sideEffect) {
                await sideEffect.ConfigureAwait(false);
                return result;
            }

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<T, ValueTask> next) {
                var result = await first.ConfigureAwait(false);
                await next(result).ConfigureAwait(false);
                return result;
            }
        }

        /// <summary>Chains an asynchronous side-effect with captured state, preserving the result and avoiding a closure allocation.</summary>
        public ValueTask<T> Then<TState>(Func<T, TState, ValueTask> next, TState state) {
            if (first.IsCompletedSuccessfully) {
                var result = first.Result;
                var sideEffect = next(result, state);
                if (sideEffect.IsCompletedSuccessfully) return new ValueTask<T>(result);
                return WhenSideEffectPending(result, sideEffect);
            }
            return WhenFirstPending(first, next, state);

            static async ValueTask<T> WhenSideEffectPending(T result, ValueTask sideEffect) {
                await sideEffect.ConfigureAwait(false);
                return result;
            }

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<T, TState, ValueTask> next, TState state) {
                var result = await first.ConfigureAwait(false);
                await next(result, state).ConfigureAwait(false);
                return result;
            }
        }

        #endregion // Asynchronous Then Extensions

        #region ->> Synchronous Then Extensions <<-

        /// <summary>Chains a synchronous side-effect, preserving the result.</summary>
        public ValueTask<T> Then(Action<T> next) {
            if (first.IsCompletedSuccessfully) { next(first.Result); return first; }
            return WhenFirstPending(first, next);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Action<T> next) {
                var result = await first.ConfigureAwait(false);
                next(result);
                return result;
            }
        }

        /// <summary>Chains a synchronous side-effect with captured state, preserving the result and avoiding a closure allocation.</summary>
        public ValueTask<T> Then<TState>(Action<T, TState> next, TState state) {
            if (first.IsCompletedSuccessfully) { next(first.Result, state); return first; }
            return WhenFirstPending(first, next, state);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Action<T, TState> next, TState state) {
                var result = await first.ConfigureAwait(false);
                next(result, state);
                return result;
            }
        }

        /// <summary>Chains a synchronous continuation that transforms the result.</summary>
        public ValueTask<TResult> Then<TResult>(Func<T, TResult> next) {
            if (first.IsCompletedSuccessfully) return new ValueTask<TResult>(next(first.Result));
            return WhenFirstPending(first, next);

            static async ValueTask<TResult> WhenFirstPending(ValueTask<T> first, Func<T, TResult> next) {
                var result = await first.ConfigureAwait(false);
                return next(result);
            }
        }

        /// <summary>Chains a synchronous continuation with captured state that transforms the result, avoiding a closure allocation.</summary>
        public ValueTask<TResult> Then<TState, TResult>(Func<T, TState, TResult> next, TState state) {
            if (first.IsCompletedSuccessfully) return new ValueTask<TResult>(next(first.Result, state));
            return WhenFirstPending(first, next, state);

            static async ValueTask<TResult> WhenFirstPending(ValueTask<T> first, Func<T, TState, TResult> next, TState state) {
                var result = await first.ConfigureAwait(false);
                return next(result, state);
            }
        }

        #endregion // Synchronous Then Extensions

        #region ->> Asynchronous OnError Extensions <<-

        /// <summary>Executes an asynchronous error handler on failure, returning a fallback result.</summary>
        public ValueTask<T> OnError(Func<Exception, ValueTask<T>> errorHandler) {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<Exception, ValueTask<T>> errorHandler) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    return await errorHandler(ex).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state on failure, returning a fallback result and avoiding a closure allocation.</summary>
        public ValueTask<T> OnError<TState>(Func<Exception, TState, ValueTask<T>> errorHandler, TState state) {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<Exception, TState, ValueTask<T>> errorHandler, TState state) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    return await errorHandler(ex, state).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler when a specific exception type occurs, returning a fallback result.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public ValueTask<T> OnError<TException>(Func<TException, ValueTask<T>> errorHandler) where TException : Exception {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<TException, ValueTask<T>> errorHandler) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (TException ex) {
                    return await errorHandler(ex).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous error handler with captured state when a specific exception type occurs, returning a fallback result and avoiding a closure allocation.</summary>
        /// <remarks>Only catches <typeparamref name="TException"/>; other exception types propagate unhandled.</remarks>
        public ValueTask<T> OnError<TException, TState>(Func<TException, TState, ValueTask<T>> errorHandler, TState state) where TException : Exception {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<TException, TState, ValueTask<T>> errorHandler, TState state) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (TException ex) {
                    return await errorHandler(ex, state).ConfigureAwait(false);
                }
            }
        }

        #endregion // Asynchronous OnError Extensions

        #region ->> Synchronous OnError Extensions <<-

        /// <summary>Executes a synchronous error handler on failure, returning a fallback result.</summary>
        public ValueTask<T> OnError(Func<Exception, T> errorHandler) {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<Exception, T> errorHandler) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    return errorHandler(ex);
                }
            }
        }

        /// <summary>Executes a synchronous error handler with captured state on failure, returning a fallback result and avoiding a closure allocation.</summary>
        public ValueTask<T> OnError<TState>(Func<Exception, TState, T> errorHandler, TState state) {
            if (first.IsCompletedSuccessfully) return first;
            return WhenFirstPending(first, errorHandler, state);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<Exception, TState, T> errorHandler, TState state) {
                try {
                    return await first.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    return errorHandler(ex, state);
                }
            }
        }

        #endregion // Synchronous OnError Extensions

        #region ->> Finally Extensions <<-

        /// <summary>Executes a synchronous action regardless of success or failure, preserving the result.</summary>
        public ValueTask<T> Finally(Action finalAction) {
            if (first.IsCompletedSuccessfully) { finalAction(); return first; }
            return WhenFirstPending(first, finalAction);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Action finalAction) {
                try {
                    return await first.ConfigureAwait(false);
                }
                finally {
                    finalAction();
                }
            }
        }

        /// <summary>Executes a synchronous action with captured state regardless of success or failure, preserving the result and avoiding a closure allocation.</summary>
        public ValueTask<T> Finally<TState>(Action<TState> finalAction, TState state) {
            if (first.IsCompletedSuccessfully) { finalAction(state); return first; }
            return WhenFirstPending(first, finalAction, state);

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Action<TState> finalAction, TState state) {
                try {
                    return await first.ConfigureAwait(false);
                }
                finally {
                    finalAction(state);
                }
            }
        }

        /// <summary>Executes an asynchronous action regardless of success or failure, preserving the result.</summary>
        public ValueTask<T> Finally(Func<ValueTask> finalAction) {
            if (first.IsCompletedSuccessfully) {
                var result = first.Result;
                var final = finalAction();
                if (final.IsCompletedSuccessfully) return new ValueTask<T>(result);
                return WhenFinalActionPending(result, final);
            }
            return WhenFirstPending(first, finalAction);

            static async ValueTask<T> WhenFinalActionPending(T result, ValueTask final) {
                await final.ConfigureAwait(false);
                return result;
            }

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<ValueTask> finalAction) {
                try {
                    return await first.ConfigureAwait(false);
                }
                finally {
                    await finalAction().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Executes an asynchronous action with captured state regardless of success or failure, preserving the result and avoiding a closure allocation.</summary>
        public ValueTask<T> Finally<TState>(Func<TState, ValueTask> finalAction, TState state) {
            if (first.IsCompletedSuccessfully) {
                var result = first.Result;
                var final = finalAction(state);
                if (final.IsCompletedSuccessfully) return new ValueTask<T>(result);
                return WhenFinalActionPending(result, final);
            }
            return WhenFirstPending(first, finalAction, state);

            static async ValueTask<T> WhenFinalActionPending(T result, ValueTask final) {
                await final.ConfigureAwait(false);
                return result;
            }

            static async ValueTask<T> WhenFirstPending(ValueTask<T> first, Func<TState, ValueTask> finalAction, TState state) {
                try {
                    return await first.ConfigureAwait(false);
                }
                finally {
                    await finalAction(state).ConfigureAwait(false);
                }
            }
        }

        #endregion // Finally Extensions
    }
}
