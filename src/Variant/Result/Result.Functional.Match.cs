namespace Kurrent;

public static partial class FunctionalResult {
    #region . Sync .

    public static TResult Match<TValue, TError, TResult>(
        this Result<TValue, TError> result,
        Func<TValue, TResult> mapValue,
        Func<TError, TResult> mapError)
        where TError : IResultError =>
        result.IsSuccess ? mapValue(result.Value) : mapError(result.Error);

    public static TResult Match<TValue, TError, TResult, TState>(
        this Result<TValue, TError> result,
        Func<TState,TValue, TResult> mapValue,
        Func<TState,TError, TResult> mapError,
        TState state)
        where TError : IResultError =>
        result.IsSuccess ? mapValue(state, result.Value) : mapError(state, result.Error);

    #endregion

    #region . Async .

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, TResult> mapValue,
        Func<TError, TResult> mapError)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Match(mapValue, mapError);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask<TResult>> mapValueAsync,
        Func<TError, TResult> mapError)
        where TError : IResultError =>
        result.IsSuccess
            ? await mapValueAsync(result.Value).ConfigureAwait(false)
            : mapError(result.Error);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this Result<TValue, TError> result,
        Func<TValue, TResult> mapValue,
        Func<TError, ValueTask<TResult>> mapErrorAsync)
        where TError : IResultError =>
        result.IsSuccess
            ? mapValue(result.Value)
            : await mapErrorAsync(result.Error).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask<TResult>> mapValueAsync,
        Func<TError, TResult> mapError)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValueAsync, mapError).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, TResult> mapValue,
        Func<TError, ValueTask<TResult>> mapErrorAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValue, mapErrorAsync).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask<TResult>> mapValueAsync,
        Func<TError, ValueTask<TResult>> mapErrorAsync)
        where TError : IResultError =>
        result.IsSuccess
            ? await mapValueAsync(result.Value).ConfigureAwait(false)
            : await mapErrorAsync(result.Error).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask<TResult>> mapValueAsync,
        Func<TError, ValueTask<TResult>> mapErrorAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValueAsync, mapErrorAsync).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, TResult> mapValue,
        Func<TState, TError, TResult> mapError,
        TState state)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Match(mapValue, mapError, state);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask<TResult>> mapValueAsync,
        Func<TState, TError, TResult> mapError,
        TState state)
        where TError : IResultError =>
        result.IsSuccess
            ? await mapValueAsync(state, result.Value).ConfigureAwait(false)
            : mapError(state, result.Error);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, TResult> mapValue,
        Func<TState, TError, ValueTask<TResult>> mapErrorAsync,
        TState state)
        where TError : IResultError =>
        result.IsSuccess
            ? mapValue(state, result.Value)
            : await mapErrorAsync(state, result.Error).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask<TResult>> mapValueAsync,
        Func<TState, TError, TResult> mapError,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValueAsync, mapError, state).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, TResult> mapValue,
        Func<TState, TError, ValueTask<TResult>> mapErrorAsync,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValue, mapErrorAsync, state).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask<TResult>> mapValueAsync,
        Func<TState, TError, ValueTask<TResult>> mapErrorAsync,
        TState state)
        where TError : IResultError =>
        result.IsSuccess
            ? await mapValueAsync(state, result.Value).ConfigureAwait(false)
            : await mapErrorAsync(state, result.Error).ConfigureAwait(false);

    public static async ValueTask<TResult> MatchAsync<TValue, TError, TResult, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask<TResult>> mapValueAsync,
        Func<TState, TError, ValueTask<TResult>> mapErrorAsync,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MatchAsync(mapValueAsync, mapErrorAsync, state).ConfigureAwait(false);

    #endregion
}
