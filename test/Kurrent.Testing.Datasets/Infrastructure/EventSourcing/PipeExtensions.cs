namespace Kurrent.Surge;

public static partial class PipeExtensions {
    #region Asynchronous Then Extensions for Task

    /// <summary>
    /// Chains the next asynchronous task to execute after the current task completes.
    /// </summary>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">A function that returns the next task to execute.</param>
    /// <returns>A Task representing the combined execution of both tasks.</returns>
    public static Task Then(this Task first, Func<Task> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException(t.Exception) : next()).Unwrap();
    }

    /// <summary>
    /// Chains the next asynchronous task with a result to execute after the current task completes.
    /// </summary>
    /// <typeparam name="T1">The type of the result of the initial task.</typeparam>
    /// <typeparam name="T2">The type of the result of the next task.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">A function that returns the next task to execute, accepting the result of the initial task.</param>
    /// <returns>A Task representing the combined execution of both tasks, with a result of type <typeparamref name="T2"/>.</returns>
    public static Task<T2> Then<T1, T2>(this Task<T1> first, Func<T1, Task<T2>> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException<T2>(t.Exception) : next(t.Result)).Unwrap();
    }

    /// <summary>
    /// Chains the next asynchronous task that returns a result to execute after the current task completes.
    /// </summary>
    /// <typeparam name="T">The type of the result of the next task.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">A function that returns the next task to execute.</param>
    /// <returns>A Task representing the combined execution of both tasks, with a result of type <typeparamref name="T"/>.</returns>
    public static Task<T> Then<T>(this Task first, Func<Task<T>> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException<T>(t.Exception) : next()).Unwrap();
    }

    /// <summary>
    /// Chains the next asynchronous task to execute with the result of the initial task.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial task.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">A function that returns the next task to execute, accepting the result of the initial task.</param>
    /// <returns>A Task representing the combined execution of both tasks.</returns>
    public static Task Then<T>(this Task<T> first, Func<T, Task> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException(t.Exception) : next(t.Result)).Unwrap();
    }

    #endregion

    #region Synchronous Then Extensions for Task

    /// <summary>
    /// Chains the next synchronous action to execute after the current task completes.
    /// </summary>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">The action to execute after the current task completes.</param>
    /// <returns>A Task representing the completion of the initial task and the action.</returns>
    public static Task Then(this Task first, Action next) {
        return first.ContinueWith(
            t => {
                if (!t.IsFaulted)
                    next();
            }
        );
    }

    /// <summary>
    /// Chains the next synchronous action with an argument to execute after the current task completes.
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">The action to execute after the current task completes, accepting an argument.</param>
    /// <param name="arg">The argument to pass to the action.</param>
    /// <returns>A Task representing the completion of the initial task and the action.</returns>
    public static Task Then<T>(this Task first, Action<T> next, T arg) {
        return first.ContinueWith(
            t => {
                if (!t.IsFaulted)
                    next(arg);
            }
        );
    }

    /// <summary>
    /// Chains the next synchronous action to execute with the result of the initial task.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial task.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">The action to execute after the initial task completes, accepting the result of the initial task.</param>
    /// <returns>A Task representing the completion of the initial task and the action.</returns>
    public static Task Then<T>(this Task<T> first, Action<T> next) {
        return first.ContinueWith(
            t => {
                if (!t.IsFaulted)
                    next(t.Result);
            }
        );
    }

    /// <summary>
    /// Chains the next synchronous function that returns a result to execute after the current task completes.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the next function.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">The function to execute after the current task completes.</param>
    /// <returns>A Task representing the completion of the initial task and the function, with a result of type <typeparamref name="TResult"/>.</returns>
    public static Task<TResult> Then<TResult>(this Task first, Func<TResult> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException<TResult>(t.Exception) : Task.FromResult(next())).Unwrap();
    }

    /// <summary>
    /// Chains the next synchronous function to execute with the result of the initial task.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial task.</typeparam>
    /// <typeparam name="TResult">The type of the result of the next function.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="next">The function to execute after the initial task completes, accepting the result of the initial task.</param>
    /// <returns>A Task representing the completion of the initial task and the function, with a result of type <typeparamref name="TResult"/>.</returns>
    public static Task<TResult> Then<T, TResult>(this Task<T> first, Func<T, TResult> next) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromException<TResult>(t.Exception) : Task.FromResult(next(t.Result))).Unwrap();
    }

    #endregion

    #region Asynchronous OnError Extensions for Task

    /// <summary>
    /// Handles errors that occur during the execution of the initial task by executing the specified asynchronous error handler.
    /// </summary>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error occurs.</param>
    /// <returns>A task representing the completion of the error handling function.</returns>
    public static async Task OnError(this Task first, Func<Exception, Task> errorHandler) {
        try {
            await first;
        }
        catch (Exception ex) {
            await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors that occur during the execution of the initial task by executing the specified asynchronous error handler and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A task representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static async Task<T> OnError<T>(this Task<T> first, Func<Exception, Task<T>> errorHandler) {
        try {
            return await first;
        }
        catch (Exception ex) {
            return await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors of a specific type that occur during the execution of the initial task by executing the specified asynchronous error handler.
    /// </summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error of type <typeparamref name="TException"/> occurs.</param>
    /// <returns>A task representing the completion of the error handling function.</returns>
    public static async Task OnError<TException>(this Task first, Func<TException, Task> errorHandler) where TException : Exception {
        try {
            await first;
        }
        catch (TException ex) {
            await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors of a specific type that occur during the execution of the initial task by executing the specified asynchronous error handler and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error of type <typeparamref name="TException"/> occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A task representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static async Task<T> OnError<T, TException>(this Task<T> first, Func<TException, Task<T>> errorHandler) where TException : Exception {
        try {
            return await first;
        }
        catch (TException ex) {
            return await errorHandler(ex);
        }
    }

    #endregion

    #region Synchronous OnError Extensions for Task

    /// <summary>
    /// Handles errors that occur during the execution of the initial task by executing the specified action.
    /// </summary>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The action to execute if an error occurs.</param>
    /// <returns>A Task representing the completion of the error handling action.</returns>
    public static Task OnError(this Task first, Action<Exception> errorHandler) {
        return first.ContinueWith(
            t => {
                if (t.IsFaulted)
                    errorHandler(t.Exception);
            }
        );
    }

    /// <summary>
    /// Handles errors that occur during the execution of the initial task by executing the specified function and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <param name="first">The initial task to execute.</param>
    /// <param name="errorHandler">The function to execute if an error occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A Task representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static Task<T> OnError<T>(this Task<T> first, Func<Exception, T> errorHandler) {
        return first.ContinueWith(t => t.IsFaulted ? Task.FromResult(errorHandler(t.Exception)) : t).Unwrap();
    }

    #endregion

    #region Finally Extensions for Task

    /// <summary>
    /// Executes a final action regardless of whether the preceding task succeeded or failed.
    /// </summary>
    public static async Task Finally(this Task first, Action finalAction) {
        try {
            await first;
        }
        finally {
            finalAction();
        }
    }

    /// <summary>
    /// Executes a final action regardless of whether the preceding task succeeded or failed and returns the result if the preceding task succeeded.
    /// </summary>
    public static async Task<T> Finally<T>(this Task<T> first, Action finalAction) {
        try {
            return await first;
        }
        finally {
            finalAction();
        }
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding task succeeded or failed.
    /// </summary>
    public static async Task Finally(this Task first, Func<Task> finalAction) {
        try {
            await first;
        }
        finally {
            await finalAction();
        }
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding task succeeded or failed and returns the result if the preceding task succeeded.
    /// </summary>
    public static async Task<T> Finally<T>(this Task<T> first, Func<Task> finalAction) {
        try {
            return await first;
        }
        finally {
            await finalAction();
        }
    }

    /// <summary>
    /// Executes a final action regardless of whether the preceding task succeeded or failed and returns the result of the preceding task.
    /// </summary>
    public static async Task<T?> FinallyWithResult<T>(this Task<T> first, Action finalAction) {
        T? result;
        try {
            result = await first;
        }
        finally {
            finalAction();
        }

        return result;
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding task succeeded or failed and returns the result of the preceding task.
    /// </summary>
    public static async Task<T?> FinallyWithResult<T>(this Task<T> first, Func<Task> finalAction) {
        T? result;
        try {
            result = await first;
        }
        finally {
            await finalAction();
        }

        return result;
    }

    #endregion

    #region Timeout Handling Extensions for Task

    /// <summary>
    /// Executes a task with a timeout.
    /// </summary>
    public static async Task WithTimeout(this Task task, TimeSpan timeout) {
        if (await Task.WhenAny(task, Task.Delay(timeout)) == task) {
            await task; // Task completed within timeout.
        }
        else {
            throw new TimeoutException("The operation has timed out.");
        }
    }

    /// <summary>
    /// Executes a task with a timeout and returns the result.
    /// </summary>
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout) {
        if (await Task.WhenAny(task, Task.Delay(timeout)) == task) {
            return await task; // Task completed within timeout.
        }
        else {
            throw new TimeoutException("The operation has timed out.");
        }
    }

    #endregion
}

public static partial class PipeExtensions {
    #region Asynchronous Then Extensions for ValueTask

    /// <summary>
    /// Chains the next asynchronous task to execute after the current ValueTask completes.
    /// </summary>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">A function that returns the next ValueTask to execute.</param>
    /// <returns>A ValueTask representing the combined execution of both tasks.</returns>
    public static ValueTask Then(this ValueTask first, Func<ValueTask> next) {
        return new ValueTask(first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException(t.Exception) : next().AsTask()).Unwrap());
    }

    /// <summary>
    /// Chains the next asynchronous task with a result to execute after the current ValueTask completes.
    /// </summary>
    /// <typeparam name="T1">The type of the result of the initial ValueTask.</typeparam>
    /// <typeparam name="T2">The type of the result of the next ValueTask.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">A function that returns the next ValueTask to execute, accepting the result of the initial ValueTask.</param>
    /// <returns>A ValueTask representing the combined execution of both tasks, with a result of type <typeparamref name="T2"/>.</returns>
    public static ValueTask<T2> Then<T1, T2>(this ValueTask<T1> first, Func<T1, ValueTask<T2>> next) {
        return new ValueTask<T2>(first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException<T2>(t.Exception) : next(t.Result).AsTask()).Unwrap());
    }

    /// <summary>
    /// Chains the next asynchronous task that returns a result to execute after the current ValueTask completes.
    /// </summary>
    /// <typeparam name="T">The type of the result of the next ValueTask.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">A function that returns the next ValueTask to execute.</param>
    /// <returns>A ValueTask representing the combined execution of both tasks, with a result of type <typeparamref name="T"/>.</returns>
    public static ValueTask<T> Then<T>(this ValueTask first, Func<ValueTask<T>> next) {
        return new ValueTask<T>(first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException<T>(t.Exception) : next().AsTask()).Unwrap());
    }

    /// <summary>
    /// Chains the next asynchronous task to execute with the result of the initial ValueTask.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial ValueTask.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">A function that returns the next ValueTask to execute, accepting the result of the initial ValueTask.</param>
    /// <returns>A ValueTask representing the combined execution of both tasks.</returns>
    public static ValueTask Then<T>(this ValueTask<T> first, Func<T, ValueTask> next) {
        return new ValueTask(first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException(t.Exception) : next(t.Result).AsTask()).Unwrap());
    }

    #endregion

    #region Synchronous Then Extensions for ValueTask

    /// <summary>
    /// Chains the next synchronous action to execute after the current ValueTask completes.
    /// </summary>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">The action to execute after the current ValueTask completes.</param>
    /// <returns>A ValueTask representing the completion of the initial task and the action.</returns>
    public static ValueTask Then(this ValueTask first, Action next) {
        return new ValueTask(
            first.AsTask().ContinueWith(
                t => {
                    if (!t.IsFaulted)
                        next();
                }
            )
        );
    }

    /// <summary>
    /// Chains the next synchronous action with an argument to execute after the current ValueTask completes.
    /// </summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">The action to execute after the current ValueTask completes, accepting an argument.</param>
    /// <param name="arg">The argument to pass to the action.</param>
    /// <returns>A ValueTask representing the completion of the initial task and the action.</returns>
    public static ValueTask Then<T>(this ValueTask first, Action<T> next, T arg) {
        return new ValueTask(
            first.AsTask().ContinueWith(
                t => {
                    if (!t.IsFaulted)
                        next(arg);
                }
            )
        );
    }

    /// <summary>
    /// Chains the next synchronous action to execute with the result of the initial ValueTask.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial ValueTask.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">The action to execute after the initial ValueTask completes, accepting the result of the initial ValueTask.</param>
    /// <returns>A ValueTask representing the completion of the initial task and the action.</returns>
    public static ValueTask Then<T>(this ValueTask<T> first, Action<T> next) {
        return new ValueTask(
            first.AsTask().ContinueWith(
                t => {
                    if (!t.IsFaulted)
                        next(t.Result);
                }
            )
        );
    }

    /// <summary>
    /// Chains the next synchronous function that returns a result to execute after the current ValueTask completes.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the next function.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">The function to execute after the current ValueTask completes.</param>
    /// <returns>A ValueTask representing the completion of the initial task and the function, with a result of type <typeparamref name="TResult"/>.</returns>
    public static ValueTask<TResult> Then<TResult>(this ValueTask first, Func<TResult> next) {
        return new ValueTask<TResult>(
            first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException<TResult>(t.Exception) : Task.FromResult(next())).Unwrap()
        );
    }

    /// <summary>
    /// Chains the next synchronous function to execute with the result of the initial ValueTask.
    /// </summary>
    /// <typeparam name="T">The type of the result of the initial ValueTask.</typeparam>
    /// <typeparam name="TResult">The type of the result of the next function.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="next">The function to execute after the initial ValueTask completes, accepting the result of the initial ValueTask.</param>
    /// <returns>A ValueTask representing the completion of the initial task and the function, with a result of type <typeparamref name="TResult"/>.</returns>
    public static ValueTask<TResult> Then<T, TResult>(this ValueTask<T> first, Func<T, TResult> next) {
        return new ValueTask<TResult>(
            first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromException<TResult>(t.Exception) : Task.FromResult(next(t.Result))).Unwrap()
        );
    }

    #endregion

    #region Synchronous OnError Extensions for ValueTask

    /// <summary>
    /// Handles errors that occur during the execution of the initial ValueTask by executing the specified action.
    /// </summary>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The action to execute if an error occurs.</param>
    /// <returns>A ValueTask representing the completion of the error handling action.</returns>
    public static ValueTask OnError(this ValueTask first, Action<Exception> errorHandler) {
        return new ValueTask(
            first.AsTask().ContinueWith(
                t => {
                    if (t.IsFaulted)
                        errorHandler(t.Exception);
                }
            )
        );
    }

    /// <summary>
    /// Handles errors that occur during the execution of the initial ValueTask by executing the specified function and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The function to execute if an error occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A ValueTask representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static ValueTask<T> OnError<T>(this ValueTask<T> first, Func<Exception, T> errorHandler) {
        return new ValueTask<T>(first.AsTask().ContinueWith(t => t.IsFaulted ? Task.FromResult(errorHandler(t.Exception)) : t).Unwrap());
    }

    #endregion

    #region Asynchronous OnError Extensions for ValueTask

    /// <summary>
    /// Handles errors that occur during the execution of the initial ValueTask by executing the specified asynchronous error handler.
    /// </summary>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error occurs.</param>
    /// <returns>A ValueTask representing the completion of the error handling function.</returns>
    public static async ValueTask OnError(this ValueTask first, Func<Exception, ValueTask> errorHandler) {
        try {
            await first;
        }
        catch (Exception ex) {
            await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors that occur during the execution of the initial ValueTask by executing the specified asynchronous error handler and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A ValueTask representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static async ValueTask<T> OnError<T>(this ValueTask<T> first, Func<Exception, ValueTask<T>> errorHandler) {
        try {
            return await first;
        }
        catch (Exception ex) {
            return await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors of a specific type that occur during the execution of the initial ValueTask by executing the specified asynchronous error handler.
    /// </summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error of type <typeparamref name="TException"/> occurs.</param>
    /// <returns>A ValueTask representing the completion of the error handling function.</returns>
    public static async ValueTask OnError<TException>(this ValueTask first, Func<TException, ValueTask> errorHandler) where TException : Exception {
        try {
            await first;
        }
        catch (TException ex) {
            await errorHandler(ex);
        }
    }

    /// <summary>
    /// Handles errors of a specific type that occur during the execution of the initial ValueTask by executing the specified asynchronous error handler and returning a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the error handling function.</typeparam>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="first">The initial ValueTask to execute.</param>
    /// <param name="errorHandler">The asynchronous function to execute if an error of type <typeparamref name="TException"/> occurs, returning a result of type <typeparamref name="T"/>.</param>
    /// <returns>A ValueTask representing the completion of the error handling function, with a result of type <typeparamref name="T"/>.</returns>
    public static async ValueTask<T> OnError<T, TException>(this ValueTask<T> first, Func<TException, ValueTask<T>> errorHandler) where TException : Exception {
        try {
            return await first;
        }
        catch (TException ex) {
            return await errorHandler(ex);
        }
    }

    #endregion

    #region Finally Extensions for ValueTask

    /// <summary>
    /// Executes a final action regardless of whether the preceding ValueTask succeeded or failed.
    /// </summary>
    public static async ValueTask Finally(this ValueTask first, Action finalAction) {
        try {
            await first;
        }
        finally {
            finalAction();
        }
    }

    /// <summary>
    /// Executes a final action regardless of whether the preceding ValueTask succeeded or failed and returns the result if the preceding ValueTask succeeded.
    /// </summary>
    public static async ValueTask<T> Finally<T>(this ValueTask<T> first, Action finalAction) {
        try {
            return await first;
        }
        finally {
            finalAction();
        }
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding ValueTask succeeded or failed.
    /// </summary>
    public static async ValueTask Finally(this ValueTask first, Func<ValueTask> finalAction) {
        try {
            await first;
        }
        finally {
            await finalAction();
        }
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding ValueTask succeeded or failed and returns the result if the preceding ValueTask succeeded.
    /// </summary>
    public static async ValueTask<T> Finally<T>(this ValueTask<T> first, Func<ValueTask> finalAction) {
        try {
            return await first;
        }
        finally {
            await finalAction();
        }
    }

    /// <summary>
    /// Executes a final action regardless of whether the preceding ValueTask succeeded or failed and returns the result of the preceding ValueTask.
    /// </summary>
    public static async ValueTask<T?> FinallyWithResult<T>(this ValueTask<T> first, Action finalAction) {
        T? result;
        try {
            result = await first;
        }
        finally {
            finalAction();
        }

        return result;
    }

    /// <summary>
    /// Executes a final asynchronous action regardless of whether the preceding ValueTask succeeded or failed and returns the result of the preceding ValueTask.
    /// </summary>
    public static async ValueTask<T?> FinallyWithResult<T>(this ValueTask<T> first, Func<ValueTask> finalAction) {
        T? result;
        try {
            result = await first;
        }
        finally {
            await finalAction();
        }

        return result;
    }

    #endregion

    #region Timeout Handling Extensions for ValueTask

    /// <summary>
    /// Executes a ValueTask with a timeout.
    /// </summary>
    public static async ValueTask WithTimeout(this ValueTask task, TimeSpan timeout) {
        if (await Task.WhenAny(task.AsTask(), Task.Delay(timeout)) == task.AsTask()) {
            await task; // Task completed within timeout.
        }
        else {
            throw new TimeoutException("The operation has timed out.");
        }
    }

    /// <summary>
    /// Executes a ValueTask with a timeout and returns the result.
    /// </summary>
    public static async ValueTask<T> WithTimeout<T>(this ValueTask<T> task, TimeSpan timeout) {
        if (await Task.WhenAny(task.AsTask(), Task.Delay(timeout)) == task.AsTask()) {
            return await task; // Task completed within timeout.
        }
        else {
            throw new TimeoutException("The operation has timed out.");
        }
    }

    #endregion
}