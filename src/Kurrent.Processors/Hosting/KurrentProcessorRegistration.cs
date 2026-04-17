using Kurrent.Client;

namespace Kurrent.Processors.Options;

/// <summary>
/// Internal registration info for a processor type.
/// Holds configuration built from <see cref="KurrentProcessorHostBuilder"/>.
/// </summary>
/// <typeparam name="TProcessor">The processor type being registered.</typeparam>
internal sealed record KurrentProcessorRegistration<TProcessor>(
    ProcessorOptions Settings,
    string? ConnectionString,
    Func<KurrentClientOptions, KurrentClientOptions>? ClientConfigure,
    bool UseOwnClient
) where TProcessor : IRecordProcessor;
