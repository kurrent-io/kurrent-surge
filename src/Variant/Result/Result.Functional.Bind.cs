namespace Kurrent;

public static partial class FunctionalResult {
    #region . Sync .

    public static Result<TNewValue, TError> Bind<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, Result<TNewValue, TError>> bind)
        where TError : IResultError =>
        result.IsSuccess ? bind(result.Value) : Result<TNewValue, TError>.Failure(result.Error);

    public static Result<TNewValue, TError> Bind<TValue, TNewValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState,TValue, Result<TNewValue, TError>> bind,
        TState state) where TError : IResultError =>
        result.IsSuccess ? bind(state, result.Value) : Result<TNewValue, TError>.Failure(result.Error);

    #endregion

    #region . Async .

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, Result<TNewValue, TError>> bind)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Bind(bind);

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask<Result<TNewValue, TError>>> bindAsync)
        where TError : IResultError =>
        result.IsSuccess
            ? await bindAsync(result.Value).ConfigureAwait(false)
            : Result<TNewValue, TError>.Failure(result.Error);

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask<Result<TNewValue, TError>>> bindAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).BindAsync(bindAsync).ConfigureAwait(false);

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, Result<TNewValue, TError>> bind,
        TState state)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Bind(bind, state);

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask<Result<TNewValue, TError>>> bindAsync,
        TState state)
        where TError : IResultError =>
        result.IsSuccess
            ? await bindAsync(state, result.Value).ConfigureAwait(false)
            : Result<TNewValue, TError>.Failure(result.Error);

    public static async ValueTask<Result<TNewValue, TError>> BindAsync<TValue, TNewValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask<Result<TNewValue, TError>>> bindAsync,
        TState state)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).BindAsync(bindAsync, state).ConfigureAwait(false);

    #endregion
}
