using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using KurrentDB.Client;

namespace Kurrent.Surge.Client;

/// <summary>
/// Base class for client modules that provides access to the Kurrent client, logger, and other common functionality.
/// This class is intended to be inherited by specific client modules that interact with KurrentDB.
/// It encapsulates shared functionality such as logging, schema serialization, and metadata decoding.
/// </summary>
public abstract class ClientModuleBase {
    protected ClientModuleBase(KurrentClient client, string? moduleName = null) {
        KurrentClient = client;
        Time          = TimeProvider.System;
        LoggerFactory = client.Options.LoggerFactory;
        Logger        = client.Options.LoggerFactory.CreateLogger(moduleName ?? GetType().Name);
    }

    KurrentClient KurrentClient { get; }

    protected internal TimeProvider   Time          { get; }
    protected internal ILoggerFactory LoggerFactory { get; }
    protected internal ILogger        Logger        { get; }

    protected ISchemaSerializerProvider SerializerProvider => KurrentClient.SerializerProvider;
    protected ServerCapabilities        ServerCapabilities => KurrentClient.LegacyCallInvoker.ServerCapabilities;
    protected IMetadataDecoder          MetadataDecoder    => KurrentClient.Options.MetadataDecoder;

    protected HttpClient GetBackdoorClient() =>
        KurrentClient.BackdoorClientFactory.GetClient();
}
