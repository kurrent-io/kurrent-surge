namespace Kurrent;

public static partial class FunctionalResult {
    #region ->> Sync <<-
    public static Result<TNewValue, TError> Map<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        TNewValue newValue)
        where TError : IResultError =>
        result.IsSuccess ? Result<TNewValue, TError>.Success(newValue) : Result<TNewValue, TError>.Failure(result.Error);

    public static Result<TNewValue, TError> Map<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, TNewValue> map)
        where TError : IResultError =>
        result.IsSuccess ? Result<TNewValue, TError>.Success(map(result.Value)) : Result<TNewValue, TError>.Failure(result.Error);

    public static Result<TValue, TNewError> MapError<TValue, TError, TNewError>(
        this Result<TValue, TError> result,
        Func<TError, TNewError> map)
        where TError : IResultError where TNewError : IResultError =>
        result.IsSuccess ? Result<TValue, TNewError>.Success(result.Value) : Result<TValue, TNewError>.Failure(map(result.Error));

    public static Result<TNewValue, TError> Map<TValue, TNewValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState,TValue, TNewValue> map,
        TState state)
        where TError : IResultError =>
        result.IsSuccess ? Result<TNewValue, TError>.Success(map(state, result.Value)) : Result<TNewValue, TError>.Failure(result.Error);

    public static Result<TValue, TNewError> MapError<TValue, TError, TNewError, TState>(
        this Result<TValue, TError> result,
        Func<TState,TError, TNewError> map,
        TState state)
        where TError : IResultError where TNewError : IResultError =>
        result.IsSuccess ? Result<TValue, TNewError>.Success(result.Value) : Result<TValue, TNewError>.Failure(map(state, result.Error));

    #endregion

    #region ->> Async <<-

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        TNewValue newValue)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Map(newValue);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, TNewValue> map)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Map(map);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this Result<TValue, TError> result,
        Func<TValue, ValueTask<TNewValue>> mapAsync)
        where TError : IResultError =>
        result.IsSuccess
            ? Result<TNewValue, TError>.Success(await mapAsync(result.Value).ConfigureAwait(false))
            : Result<TNewValue, TError>.Failure(result.Error);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TValue, ValueTask<TNewValue>> mapAsync)
        where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MapAsync(mapAsync).ConfigureAwait(false);

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TError, TNewError> map)
        where TError : IResultError where TNewError : IResultError =>
        (await result.ConfigureAwait(false)).MapError(map);

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError>(
        this Result<TValue, TError> result,
        Func<TError, ValueTask<TNewError>> mapAsync)
        where TError : IResultError where TNewError : IResultError =>
        result.IsSuccess
            ? Result<TValue, TNewError>.Success(result.Value)
            : Result<TValue, TNewError>.Failure(await mapAsync(result.Error).ConfigureAwait(false));

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TError, ValueTask<TNewError>> mapAsync)
        where TError : IResultError where TNewError : IResultError =>
        await (await result.ConfigureAwait(false)).MapErrorAsync(mapAsync).ConfigureAwait(false);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, TNewValue> map,
        TState state)
        where TError : IResultError =>
        (await result.ConfigureAwait(false)).Map(map, state);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TValue, ValueTask<TNewValue>> mapAsync,
        TState state) where TError : IResultError =>
        result.IsSuccess
            ? Result<TNewValue, TError>.Success(await mapAsync(state, result.Value).ConfigureAwait(false))
            : Result<TNewValue, TError>.Failure(result.Error);

    public static async ValueTask<Result<TNewValue, TError>> MapAsync<TValue, TNewValue, TError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TValue, ValueTask<TNewValue>> mapAsync,
        TState state) where TError : IResultError =>
        await (await result.ConfigureAwait(false)).MapAsync(mapAsync, state).ConfigureAwait(false);

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TError, TNewError> map,
        TState state)
        where TError : IResultError where TNewError : IResultError =>
        (await result.ConfigureAwait(false)).MapError(map, state);

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError, TState>(
        this Result<TValue, TError> result,
        Func<TState, TError, ValueTask<TNewError>> mapAsync,
        TState state)
        where TError : IResultError where TNewError : IResultError =>
        result.IsSuccess
            ? Result<TValue, TNewError>.Success(result.Value)
            : Result<TValue, TNewError>.Failure(await mapAsync(state, result.Error).ConfigureAwait(false));

    public static async ValueTask<Result<TValue, TNewError>> MapErrorAsync<TValue, TError, TNewError, TState>(
        this ValueTask<Result<TValue, TError>> result,
        Func<TState, TError, ValueTask<TNewError>> mapAsync,
        TState state)
        where TError : IResultError where TNewError : IResultError =>
        await (await result.ConfigureAwait(false)).MapErrorAsync(mapAsync, state).ConfigureAwait(false);

    #endregion
}
