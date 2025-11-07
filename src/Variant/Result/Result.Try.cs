namespace Kurrent;

public static class Result {
	#region . Factory Methods .

	/// <summary>
	/// Creates a new <see cref="Result{TValue,TError}"/> representing a successful operation with the specified value.
	/// </summary>
	/// <typeparam name="TValue">The type of the success value.</typeparam>
	/// <typeparam name="TError">The type of the error value, must implement IResultError.</typeparam>
	/// <param name="value">The success value.</param>
	/// <returns>A <see cref="Result{TValue,TError}"/> representing success.</returns>
	[PublicAPI]
	public static Result<TValue, TError> Success<TValue, TError>(TValue value) where TError : IResultError =>
		Result<TValue, TError>.Success(value);

	/// <summary>
	/// Creates a new <see cref="Result{TValue,TError}"/> representing a failed operation with the specified error.
	/// </summary>
	/// <typeparam name="TValue">The type of the success value.</typeparam>
	/// <typeparam name="TError">The type of the error value, must implement IResultError.</typeparam>
	/// <param name="error">The error value.</param>
	/// <returns>A <see cref="Result{TValue,TError}"/> representing failure.</returns>
	[PublicAPI]
	public static Result<TValue, TError> Failure<TValue, TError>(TError error) where TError : IResultError =>
		Result<TValue, TError>.Failure(error);

	#endregion

	#region . Try .

	/// <summary>
	/// Executes the function and converts any thrown exceptions to Result errors.
	/// </summary>
	public static Result<TValue, TError> Try<TValue, TError>(
		Func<TValue> operation,
		Func<Exception, TError> exceptionFactory) where TError : IResultError {
        try {
            return operation();
        }
        catch (Exception ex) {
            return exceptionFactory(ex);
        }
	}

	/// <summary>
	/// Executes the function with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static Result<TValue, TError> Try<TValue, TError, TState>(
		Func<TState, TValue> operation,
		Func<TState, Exception, TError> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			return operation(state);
		}
		catch (Exception ex) {
			return exceptionFactory(state, ex);
		}
	}

	/// <summary>
	/// Asynchronously executes the function and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<TValue, TError>> TryAsync<TValue, TError>(
		Func<ValueTask<TValue>> operation,
		Func<Exception, TError> exceptionFactory) where TError : IResultError {
		try {
			return await operation().ConfigureAwait(false);
		}
		catch (Exception ex) {
			return exceptionFactory(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes the function with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<TValue, TError>> TryAsync<TValue, TError, TState>(
		Func<TState, ValueTask<TValue>> operation,
		Func<TState, Exception, TError> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			return await operation(state).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return exceptionFactory(state, ex);
		}
	}

	/// <summary>
	/// Asynchronously executes the function and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<TValue, TError>> TryAsync<TValue, TError>(
		Func<ValueTask<TValue>> operation,
		Func<Exception, ValueTask<TError>> exceptionFactory) where TError : IResultError {
		try {
			return await operation().ConfigureAwait(false);
		}
		catch (Exception ex) {
			return await exceptionFactory(ex).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Asynchronously executes the function with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<TValue, TError>> TryAsync<TValue, TError, TState>(
		Func<TState, ValueTask<TValue>> operation,
		Func<TState, Exception, ValueTask<TError>> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			return await operation(state).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return await exceptionFactory(state, ex).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Executes an action and converts any thrown exceptions to Result errors.
	/// </summary>
	public static Result<Success, TError> Try<TError>(
		Action operation,
		Func<Exception, TError> exceptionFactory) where TError : IResultError {
		try {
			operation();
			return Results.Success;
		}
		catch (Exception ex) {
			return exceptionFactory(ex);
		}
	}

	/// <summary>
	/// Executes an action with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static Result<Success, TError> Try<TError, TState>(
		Action<TState> operation,
		Func<TState, Exception, TError> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			operation(state);
			return Results.Success;
		}
		catch (Exception ex) {
			return exceptionFactory(state, ex);
		}
	}

	/// <summary>
	/// Asynchronously executes an action and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<Success, TError>> TryAsync<TError>(
		Func<ValueTask> operation,
		Func<Exception, TError> exceptionFactory) where TError : IResultError {
		try {
			await operation().ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return exceptionFactory(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes an action and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<Success, TError>> TryAsync<TError>(
		Func<ValueTask> operation,
		Func<Exception, ValueTask<TError>> exceptionFactory) where TError : IResultError {
		try {
			await operation().ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return await exceptionFactory(ex).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Asynchronously executes an action with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<Success, TError>> TryAsync<TError, TState>(
		Func<TState, ValueTask> operation,
		Func<TState, Exception, TError> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			await operation(state).ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return exceptionFactory(state, ex);
		}
	}

	/// <summary>
	/// Asynchronously executes an action with additional state and converts any thrown exceptions to Result errors.
	/// </summary>
	public static async ValueTask<Result<Success, TError>> TryAsync<TError, TState>(
		Func<TState, ValueTask> operation,
		Func<TState, Exception, ValueTask<TError>> exceptionFactory,
		TState state) where TError : IResultError {
		try {
			await operation(state).ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return await exceptionFactory(state, ex).ConfigureAwait(false);
		}
	}

	#endregion

	#region . Try (Fluent API) .

	/// <summary>
	/// Executes the function and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static Result<TValue, ExceptionError> Try<TValue>(Func<TValue> operation) {
		try {
			return operation();
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Executes the function with additional state and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static Result<TValue, ExceptionError> Try<TValue, TState>(
		Func<TState, TValue> operation,
		TState state) {
		try {
			return operation(state);
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Executes an action and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static Result<Success, ExceptionError> Try(Action operation) {
		try {
			operation();
			return Results.Success;
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Executes an action with additional state and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static Result<Success, ExceptionError> Try<TState>(
		Action<TState> operation,
		TState state) {
		try {
			operation(state);
			return Results.Success;
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes the function and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static async ValueTask<Result<TValue, ExceptionError>> TryAsync<TValue>(
		Func<ValueTask<TValue>> operation) {
		try {
			return await operation().ConfigureAwait(false);
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes the function with additional state and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static async ValueTask<Result<TValue, ExceptionError>> TryAsync<TValue, TState>(
		Func<TState, ValueTask<TValue>> operation,
		TState state) {
		try {
			return await operation(state).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes an action and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static async ValueTask<Result<Success, ExceptionError>> TryAsync(
		Func<ValueTask> operation) {
		try {
			await operation().ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	/// <summary>
	/// Asynchronously executes an action with additional state and converts any thrown exceptions to ExceptionError for fluent error mapping.
	/// </summary>
	public static async ValueTask<Result<Success, ExceptionError>> TryAsync<TState>(
		Func<TState, ValueTask> operation,
		TState state) {
		try {
			await operation(state).ConfigureAwait(false);
			return Results.Success;
		}
		catch (Exception ex) {
			return new ExceptionError(ex);
		}
	}

	#endregion

	#region . ToResult .

	public static Result<Success, TError> ToResult<TError>(
		this Action operation,
		Func<Exception, TError> errorFactory)
		where TError : IResultError =>
		Try(operation, errorFactory);


	public static Result<TValue, TError> ToResult<TValue, TError>(
		this Func<TValue> operation,
		Func<Exception, TError> errorFactory)
		where TError : IResultError =>
		Try(operation, errorFactory);


	public static ValueTask<Result<Success, TError>> ToResultAsync<TError>(
		this ValueTask operation,
		Func<Exception, TError> errorFactory)
		where TError : IResultError =>
		TryAsync(() => operation, errorFactory);

	public static ValueTask<Result<Success, TError>> ToResultAsync<TError>(
		this ValueTask operation,
		Func<Exception, ValueTask<TError>> errorFactory)
		where TError : IResultError =>
		TryAsync(() => operation, errorFactory);

	public static ValueTask<Result<TValue, TError>> ToResultAsync<TValue, TError>(
		this ValueTask<TValue> operation,
		Func<Exception, TError> errorFactory)
		where TError : IResultError =>
		TryAsync(() => operation, errorFactory);

	public static ValueTask<Result<TValue, TError>> ToResultAsync<TValue, TError>(
		this ValueTask<TValue> operation,
		Func<Exception, ValueTask<TError>> errorFactory)
		where TError : IResultError =>
		TryAsync(() => operation, errorFactory);


	public static Result<TValue, ResultError> ToResult<TValue>(this Func<TValue> operation) =>
		ToResult(operation, ex => (ResultError) ResultError.Create(ex));

	public static Result<Success, ResultError> ToResult(this Action operation) =>
		ToResult<ResultError>(operation, ex => (ResultError) ResultError.Create(ex));

	public static ValueTask<Result<TValue, ResultError>> ToResultAsync<TValue>(this ValueTask<TValue> operation) =>
		ToResultAsync(operation, ex => (ResultError) ResultError.Create(ex));

	public static ValueTask<Result<Success, ResultError>> ToResultAsync(this ValueTask operation) =>
		ToResultAsync(operation, ex => (ResultError) ResultError.Create(ex));

	#endregion
}
