namespace Kurrent;

public static partial class FunctionalResult {
    #region . Sync .

    public static Result<TValue, TError> Inspect<TValue, TError> (
        this Result<TValue, TError> result,
        Action<TValue> action)
        where TError : IResultError {
        if (result.IsSuccess) action(result.Value);
        return result;
    }

    public static Result<TValue, TError> Inspect<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Action<TState, TValue> action,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess) action(state, result.Value);
        return result;
    }

    public static Result<TValue, TError> InspectError<TValue, TError>(
        this Result<TValue, TError> result,
        Action<TError> action)
        where TError : IResultError {
        if (!result.IsSuccess) action(result.Error);
        return result;
    }

    public static Result<TValue, TError> InspectError<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Action<TState, TError> action, TState state)
        where TError : IResultError {
        if (!result.IsSuccess) action(state, result.Error);
        return result;
    }

    #endregion

    #region . Async .

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TValue> action
    ) where TError : IResultError =>
        (await result.ConfigureAwait(false)).Inspect(action);

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask> action)
        where TError : IResultError {
        if (result.IsSuccess) await action(result.Value).ConfigureAwait(false);
        return result;
    }

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask> action
    ) where TError : IResultError =>
        await (await result.ConfigureAwait(false)).InspectAsync(action).ConfigureAwait(false);

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TState, TValue> action,
        TState state
    ) where TError : IResultError =>
        (await result.ConfigureAwait(false)).Inspect(action, state);

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask> action,
        TState state)
        where TError : IResultError {
        if (result.IsSuccess) await action(state, result.Value).ConfigureAwait(false);
        return result;
    }

    public static async ValueTask<Result<TValue, TError>> InspectAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask> action,
        TState state
    ) where TError : IResultError =>
        await (await result.ConfigureAwait(false)).InspectAsync(action, state).ConfigureAwait(false);

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TError> action
    ) where TError : IResultError =>
        (await result.ConfigureAwait(false)).InspectError(action);

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError>(
        this Result<TValue, TError> result,
        Func<TError, ValueTask> action)
        where TError : IResultError {
        if (!result.IsSuccess)
            await action(result.Error).ConfigureAwait(false);
        return result;
    }

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TError, ValueTask> action
    ) where TError : IResultError =>
        await (await result.ConfigureAwait(false)).InspectErrorAsync(action).ConfigureAwait(false);

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Action<TState, TError> action,
        TState state
    ) where TError : IResultError =>
        (await result.ConfigureAwait(false)).InspectError(action, state);

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TError, ValueTask> action,
        TState state)
        where TError : IResultError {
        if (!result.IsSuccess) await action(state, result.Error).ConfigureAwait(false);
        return result;
    }

    public static async ValueTask<Result<TValue, TError>> InspectErrorAsync<TValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TError, ValueTask> action,
        TState state
    ) where TError : IResultError =>
        await (await result.ConfigureAwait(false)).InspectErrorAsync(action, state).ConfigureAwait(false);

    #endregion
}
