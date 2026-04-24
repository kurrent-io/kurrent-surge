using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kurrent.CodeAnalysis.Pipeline;

/// <summary>
/// Wraps a Roslyn <see cref="IncrementalValuesProvider{T}"/> alongside the
/// <see cref="IncrementalGeneratorInitializationContext"/>, so a pipeline can carry the context
/// through <c>Select</c>/<c>Where</c> chains and terminate with <c>RegisterSources</c> without
/// the caller having to plumb the context manually.
/// </summary>
public readonly struct TypePipeline<T> {
    readonly IncrementalGeneratorInitializationContext _context;
    readonly IncrementalValuesProvider<T>              _provider;

    internal TypePipeline(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<T> provider) {
        _context  = context;
        _provider = provider;
    }

    public TypePipeline<TNew> Select<TNew>(Func<T, TNew> selector) =>
        new(_context, _provider.Select((value, _) => selector(value)));

    public TypePipeline<TNew> Select<TNew>(Func<T, CancellationToken, TNew> selector) =>
        new(_context, _provider.Select(selector));

    public TypePipeline<T> Where(Func<T, bool> predicate) =>
        new(_context, _provider.Where(predicate));

    public TypePipeline<(T Left, TOther Right)> Combine<TOther>(IncrementalValueProvider<TOther> other) =>
        new(_context, _provider.Combine(other));

    public void RegisterSources(Action<SourceProductionContext, T> generate) =>
        _context.RegisterSourceOutput(_provider, generate);

    public void RegisterSources() {
        if (!typeof(IGeneratedFile).IsAssignableFrom(typeof(T)))
            throw new InvalidOperationException(
                $"RegisterSources() with no arguments requires the pipeline element type to implement {nameof(IGeneratedFile)}. " +
                $"Current element type is {typeof(T).FullName}. Use RegisterSources(Action<SourceProductionContext, T>) instead.");

        _context.RegisterSourceOutput(
            _provider, (spc, value) => {
                if (value is IGeneratedFile file)
                    spc.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8));
            });
    }

    /// <summary>
    /// Registers an aggregate diagnostic that fires once per compilation when the pipeline
    /// produced zero items. Wraps the <c>.Collect()</c> + empty-check pattern so callers don't
    /// need to drop to raw <c>context.RegisterSourceOutput</c>.
    /// </summary>
    public TypePipeline<T> ReportWhenEmpty(DiagnosticDescriptor descriptor, params object?[] messageArgs) {
        _context.RegisterSourceOutput(
            _provider.Collect(),
            (spc, collected) => {
                if (collected.Length == 0)
                    spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, messageArgs));
            });

        return this;
    }

    public IncrementalValuesProvider<T> AsProvider() =>
        _provider;
}