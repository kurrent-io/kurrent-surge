namespace Kurrent.CodeAnalysis;

/// <summary>
/// Marker contract for pipeline outputs that can be wired to <c>AddSource</c> without an explicit
/// callback. When <see cref="Pipeline.TypePipeline{T}.RegisterSources()"/> is called on a pipeline
/// whose <c>T</c> implements this, each item is added as a generated file automatically.
/// </summary>
public interface IGeneratedFile {
    string HintName { get; }
    string Source   { get; }
}

public sealed record GeneratedFile(string HintName, string Source) : IGeneratedFile;
