namespace Kurrent;

public static partial class FunctionalResult {
    #region . Sync .

    public static void Switch<TValue, TError>(
        this Result<TValue, TError> result,
        Action<TValue> onSuccess,
        Action<TError> onFailure)
        where TError : IResultError {
        if (result.IsSuccess) onSuccess(result.Value);
        else onFailure(result.Error);
    }

    public static void Switch<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Action<TState, TValue> onSuccess,
        Action<TState, TError> onFailure,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess) onSuccess(state, result.Value);
        else onFailure(state, result.Error);
    }
    #endregion

    #region . Async .

    public static async ValueTask SwitchAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TValue> onSuccess,
        Action<TError> onFailure)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure);

    public static async ValueTask SwitchAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask> onSuccessAsync,
        Action<TError> onFailure)
        where TError : IResultError {
        if (result.IsSuccess)
            await onSuccessAsync(result.Value).ConfigureAwait(false);
        else
            onFailure(result.Error);
    }

    public static async ValueTask SwitchAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Action<TValue> onSuccess,
        Func<TError, ValueTask> onFailureAsync)
        where TError : IResultError {
        if (result.IsSuccess)
            onSuccess(result.Value);
        else
            await onFailureAsync(result.Error).ConfigureAwait(false);
    }

    public static async ValueTask SwitchAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask> onSuccessAsync,
        Action<TError> onFailure)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccessAsync, onFailure).ConfigureAwait(false);

    public static async ValueTask SwitchAsync<TValue, TError, TResult>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TValue> onSuccess,
        Func<TError, ValueTask> onFailureAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccess, onFailureAsync).ConfigureAwait(false);

    public static async ValueTask SwitchAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask> onSuccessAsync,
        Func<TError, ValueTask> onFailureAsync)
        where TError : IResultError {
        if (result.IsSuccess)
            await onSuccessAsync(result.Value).ConfigureAwait(false);
        else
            await onFailureAsync(result.Error).ConfigureAwait(false);
    }

    public static async ValueTask SwitchAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask> onSuccessAsync,
        Func<TError, ValueTask> onFailureAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccessAsync, onFailureAsync).ConfigureAwait(false);

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TState, TValue> onSuccess,
        Action<TState, TError> onFailure,
        TState state)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Switch(onSuccess, onFailure, state);

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask> onSuccessAsync,
        Action<TState, TError> onFailure,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess)
            await onSuccessAsync(state, result.Value).ConfigureAwait(false);
        else
            onFailure(state, result.Error);
    }

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Action<TState, TValue> onSuccess,
        Func<TState, TError, ValueTask> onFailureAsync,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess)
            onSuccess(state, result.Value);
        else
            await onFailureAsync(state, result.Error).ConfigureAwait(false);
    }

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask> onSuccessAsync,
        Action<TState, TError> onFailure,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccessAsync, onFailure, state).ConfigureAwait(false);

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TState, TValue> onSuccess,
        Func<TState, TError, ValueTask> onFailureAsync,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccess, onFailureAsync, state).ConfigureAwait(false);

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask> onSuccessAsync,
        Func<TState, TError, ValueTask> onFailureAsync,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess)
            await onSuccessAsync(state, result.Value).ConfigureAwait(false);
        else
            await onFailureAsync(state, result.Error).ConfigureAwait(false);
    }

    public static async ValueTask SwitchAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask> onSuccessAsync,
        Func<TState, TError, ValueTask> onFailureAsync,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).SwitchAsync(onSuccessAsync, onFailureAsync, state).ConfigureAwait(false);

    #endregion
}
