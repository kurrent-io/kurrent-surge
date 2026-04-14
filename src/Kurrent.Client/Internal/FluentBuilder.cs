using Kurrent.Client;

namespace Kurrent;

/// <summary>
/// Base class for fluent builders that accumulate transformations and produce a result instance.
/// </summary>
/// <typeparam name="TBuilder">The concrete builder type for fluent chaining</typeparam>
/// <typeparam name="TResult">The type being built</typeparam>
public abstract class FluentBuilder<TBuilder, TResult>
    where TBuilder : FluentBuilder<TBuilder, TResult>, new()
    where TResult : class, new() {

    /// <summary>
    /// Initializes a new instance of the <see cref="FluentBuilder{TBuilder, TResult}"/> class with default result.
    /// </summary>
    protected FluentBuilder(TResult result) => Transforms.Add(_ => result);

    List<Func<TResult, TResult>> Transforms { get; } = [];

    /// <summary>
    /// Applies a transformation and returns the same builder instance for chaining.
    /// </summary>
    public TBuilder With(Func<TResult, TResult> transformation) {
        Transforms.Add(transformation);
        return (TBuilder)this;
    }

    /// <summary>
    /// Configures the result using a sub-builder.
    /// </summary>
    /// <typeparam name="TSubBuilder">The type of sub-builder to create.</typeparam>
    /// <param name="configure">Action to configure the sub-builder</param>
    /// <returns>The same builder instance for chaining</returns>
    protected TBuilder WithBuilder<TSubBuilder>(Action<TSubBuilder> configure) where TSubBuilder : FluentBuilder<TSubBuilder, TResult>, new() =>
        With(result => new TSubBuilder().WithInitialResult(result).With(configure).GetResult());

    /// <summary>
    /// Builds the final instance after applying all configurations.
    /// </summary>
    [PublicAPI]
    public TResult Build() {
        var result = GetResult();
        return OnBuild(result);
    }

    /// <summary>
    /// Hook for finalizing the result after all transformations are applied.
    /// This method can be overridden in derived classes to perform additional validation or final adjustments.
    /// The default implementation simply returns the result as is.
    /// </summary>
    /// <remarks>
    /// This method is called after all transformations have been applied, so it can assume the result
    /// is in a consistent state.
    /// </remarks>
    protected virtual TResult OnBuild(TResult result) => result;

    /// <summary>
    /// Initializes the builder with the provided result, resetting any existing transformations.
    /// Used internally with sub builders.
    /// </summary>
    TBuilder WithInitialResult(TResult result) {
        Transforms.Clear();
        Transforms.Add(_ => result);
        return (TBuilder)this;
    }

    /// <summary>
    /// Builds the final instance by applying all accumulated transformations.
    /// </summary>
    TResult GetResult() =>
        Transforms.Aggregate(new TResult(), (result, transform) => transform(result));
}
