using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;

namespace Kurrent.Surge;

/// <summary>
/// Creates builders for applications that host exactly one processor, configured from command-line
/// arguments, environment variables, and <c>appsettings.json</c>.
/// </summary>
[PublicAPI]
public static class SurgeProcessor {
    /// <summary>
    /// Creates a builder for an application that hosts a <typeparamref name="TProcessor"/>, resolved
    /// from the container as a singleton. The processor id is the type name in kebab-case.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type. Record and batch processors are both accepted.</typeparam>
    /// <param name="args">Optional. The command-line arguments, read as configuration.</param>
    /// <param name="configure">Optional. Sets processor options in code, over the values read from configuration.</param>
    public static SurgeProcessorBuilder<TProcessor> CreateBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string[]? args = null,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IProcessor =>
        new(args, null, configure);

    /// <summary>
    /// Creates a builder for an application that hosts a <typeparamref name="TProcessor"/> under
    /// <paramref name="processorId"/>, resolved from the container as a singleton.
    /// </summary>
    /// <typeparam name="TProcessor">The processor type. Record and batch processors are both accepted.</typeparam>
    /// <param name="args">The command-line arguments, read as configuration. <see langword="null"/> for none.</param>
    /// <param name="processorId">The processor id, and the key of the configuration section the processor reads.</param>
    /// <param name="configure">Optional. Sets processor options in code, over the values read from configuration.</param>
    public static SurgeProcessorBuilder<TProcessor> CreateBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(
        string[]? args,
        string processorId,
        Action<ProcessorOptions>? configure = null
    ) where TProcessor : class, IProcessor =>
        new(args, processorId, configure);
}
