namespace Kurrent.Client.Streams;

/// <summary>
/// Internal registration info for a processor type.
/// Holds configuration built from <see cref="KurrentProcessorHostBuilder"/>.
/// </summary>
/// <typeparam name="TProcessor">The processor type being registered.</typeparam>
internal sealed record KurrentProcessorRegistration<TProcessor>(
    ProcessorSettings Settings,
    string? ConnectionString,
    Func<KurrentClientOptions, KurrentClientOptions>? ClientConfigure,
    bool UseOwnClient
) where TProcessor : IRecordProcessor;
