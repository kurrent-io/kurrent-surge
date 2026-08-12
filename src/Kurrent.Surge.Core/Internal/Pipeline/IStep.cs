namespace Kurrent.Surge;

/// <summary>
/// One typed processing stage. Stages compose with <see cref="Step.Then{TIn,TMid,TOut}(IStep{TIn,TMid},IStep{TMid,TOut})"/>;
/// the input/output types enforce stage ordering at compile time.
/// </summary>
interface IStep<in TIn, TOut> {
    ValueTask<TOut> Execute(TIn input, CancellationToken ct);
}

static class Step {
    public static IStep<TIn, TOut> Then<TIn, TMid, TOut>(this IStep<TIn, TMid> first, IStep<TMid, TOut> second) =>
        new Composed<TIn, TMid, TOut>(first, second);

    /// <summary>
    /// Inline stage — lets a caller insert a lambda or method group into a chain without
    /// implementing <see cref="IStep{TIn,TOut}"/>.
    /// </summary>
    public static IStep<TIn, TOut> Then<TIn, TMid, TOut>(this IStep<TIn, TMid> first, Func<TMid, CancellationToken, ValueTask<TOut>> second) =>
        new Composed<TIn, TMid, TOut>(first, new LambdaStep<TMid, TOut>(second));

    sealed class Composed<TIn, TMid, TOut>(IStep<TIn, TMid> first, IStep<TMid, TOut> second) : IStep<TIn, TOut> {
        public async ValueTask<TOut> Execute(TIn input, CancellationToken ct) =>
            await second.Execute(await first.Execute(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    sealed class LambdaStep<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> run) : IStep<TIn, TOut> {
        public ValueTask<TOut> Execute(TIn input, CancellationToken ct) => run(input, ct);
    }
}
