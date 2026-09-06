namespace Kurrent.Toolkit;

/// <summary>
/// One typed processing stage. Stages compose with <see cref="Step.Then{TIn,TMid,TOut}(IPipelineStep{TIn,TMid},IPipelineStep{TMid,TOut})"/>;
/// the input/output types enforce stage ordering at compile time.
/// </summary>
interface IPipelineStep<in TIn, TOut> {
    ValueTask<TOut> Execute(TIn input, CancellationToken ct);
}

static class Step {
    public static IPipelineStep<TIn, TOut> Then<TIn, TMid, TOut>(this IPipelineStep<TIn, TMid> first, IPipelineStep<TMid, TOut> second) =>
        new Composed<TIn, TMid, TOut>(first, second);

    /// <summary>
    /// Inline stage — lets a caller insert a lambda or method group into a chain without
    /// implementing <see cref="IPipelineStep{TIn,TOut}"/>.
    /// </summary>
    public static IPipelineStep<TIn, TOut> Then<TIn, TMid, TOut>(this IPipelineStep<TIn, TMid> first, Func<TMid, CancellationToken, ValueTask<TOut>> second) =>
        new Composed<TIn, TMid, TOut>(first, new LambdaPipelineStep<TMid, TOut>(second));

    sealed class Composed<TIn, TMid, TOut>(IPipelineStep<TIn, TMid> first, IPipelineStep<TMid, TOut> second) : IPipelineStep<TIn, TOut> {
        public async ValueTask<TOut> Execute(TIn input, CancellationToken ct) =>
            await second.Execute(await first.Execute(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }

    sealed class LambdaPipelineStep<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> run) : IPipelineStep<TIn, TOut> {
        public ValueTask<TOut> Execute(TIn input, CancellationToken ct) => run(input, ct);
    }
}
