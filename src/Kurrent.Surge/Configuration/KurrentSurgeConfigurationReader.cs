using System.Net;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Kurrent.Surge.Streaming.Models;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge;

/// <summary>
/// Reads Kurrent Surge settings out of an <see cref="IConfiguration"/>.
/// </summary>
/// <remarks>
/// <para>
/// The configuration shape mirrors the options objects, so the same settings arrive from
/// appsettings.json, environment variables (<c>Kurrent__Surge__Security__Transport</c>) and
/// command-line arguments (<c>--Kurrent:Surge:ConnectionName</c>) without any extra work.
/// </para>
/// <para>
/// Reading never throws. Anything unusable is recorded on the result, so a single bad key cannot
/// cost the rest of the section and nothing is dropped silently.
/// </para>
/// </remarks>
[PublicAPI]
public static class KurrentSurgeConfigurationReader {
    /// <summary>
    /// The root section: <c>Kurrent:Surge</c>.
    /// </summary>
    public const string SectionPath = "Kurrent:Surge";

    /// <summary>
    /// The processors section, a sibling of the client settings under <see cref="SectionPath"/>.
    /// </summary>
    public const string ProcessorsKey = "Processors";

    #region ->> Client <<-

    /// <summary>
    /// Reads client settings from <c>Kurrent:Surge</c>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="into">
    /// Options to fill. Values already set survive when configuration does not mention them, so
    /// defaults established in code are preserved.
    /// </param>
    public static ConfigurationReadResult<KurrentClientOptions> ReadClientOptions(
        IConfiguration configuration,
        KurrentClientOptions? into = null
    ) {
        var options  = into ?? new KurrentClientOptions();
        var problems = new List<ConfigurationProblem>();
        var reader   = new ConfigReader(configuration.GetSection(SectionPath), problems);

        if (reader.Exists)
            ReadClientOptions(reader, options, ProcessorsKey);

        return new ConfigurationReadResult<KurrentClientOptions>(options, problems);
    }

    /// <summary>
    /// Fills client options from an already-located section.
    /// </summary>
    /// <param name="reader">The section holding the client settings.</param>
    /// <param name="options">The options to fill.</param>
    /// <param name="alsoClaimed">
    /// Sibling keys owned by something else, so they are not reported as unrecognised.
    /// </param>
    static void ReadClientOptions(ConfigReader reader, KurrentClientOptions options, params string[] alsoClaimed) {
        // A connection string is shorthand for the endpoints and security below it, so it is applied
        // first and anything stated explicitly then overrides it.
        if (reader.TextOrNull("ConnectionString", null) is { } connectionString)
            options.Connection(connectionString);

        options.ConnectionName   = reader.Text("ConnectionName", options.ConnectionName);
        options.ConnectionScheme = reader.Choice("ConnectionScheme", options.ConnectionScheme);
        options.Endpoints        = ReadEndpoints(reader.Child("Endpoints"), options.Endpoints);

        ReadGossip(reader.Child("Gossip"), options.Gossip);
        ReadSecurity(reader.Child("Security"), options);
        ReadResilience(reader.Child("Resilience"), options.Resilience);
        ReadSchema(reader.Child("Schema"), options.Schema);
        ReadCompression(reader.Child("Compression"), options.Compression);

        reader.ReportNotConfigurable("LoggerFactory");
        reader.ReportNotConfigurable("MetadataDecoder");
        reader.ReportNotConfigurable("Messages");

        reader.ReportUnrecognised([
            "ConnectionString", "ConnectionName", "ConnectionScheme", "Endpoints",
            "Gossip", "Security", "Resilience", "Schema", "Compression", "Initialization",
            "LoggerFactory", "MetadataDecoder", "Messages",
            .. alsoClaimed
        ]);
    }

    static DnsEndPoint[] ReadEndpoints(ConfigReader reader, DnsEndPoint[] fallback) {
        if (!reader.Exists)
            return fallback;

        var endpoints = new List<DnsEndPoint>();

        var placeholder = new DnsEndPoint("localhost", EndPointSyntax.DefaultPort);

        foreach (var item in reader.Items()) {
            // Each element is either "host:port" or { Host, Port }; both reach every provider.
            var endpoint = item.ScalarValue is not null
                ? item.Self(placeholder, EndPointSyntax.TryParse, "host or host:port")
                : new DnsEndPoint(
                    item.Text("Host", "localhost"),
                    item.Number("Port", EndPointSyntax.DefaultPort));

            endpoints.Add(endpoint);
        }

        return endpoints.Count > 0 ? [.. endpoints] : fallback;
    }

    static void ReadGossip(ConfigReader reader, KurrentClientGossipOptions gossip) {
        if (!reader.Exists) return;

        gossip.MaxDiscoverAttempts = reader.Number("MaxDiscoverAttempts", gossip.MaxDiscoverAttempts);
        gossip.DiscoveryInterval   = reader.Duration("DiscoveryInterval", gossip.DiscoveryInterval);
        gossip.Timeout             = reader.Duration("Timeout", gossip.Timeout);
        gossip.ReadPreference      = reader.Choice("ReadPreference", gossip.ReadPreference);

        reader.ReportUnrecognised("MaxDiscoverAttempts", "DiscoveryInterval", "Timeout", "ReadPreference");
    }

    static void ReadSecurity(ConfigReader reader, KurrentClientOptions options) {
        if (!reader.Exists) return;

        options.Security.Transport      = ReadTransport(reader.Child("Transport"), options.Security.Transport);
        options.Security.Authentication = ReadCredentials(reader.Child("Authentication"), options.Security.Authentication);

        reader.ReportUnrecognised("Transport", "Authentication");
    }

    static TransportSecurity ReadTransport(ConfigReader reader, TransportSecurity fallback) {
        if (!reader.Exists) return fallback;

        // Short form: the mode name on its own.
        if (reader.ScalarValue is { } text)
            return TransportSecurity.TryParse(text, null, out var mode)
                ? mode
                : fallback;

        var caPath = reader.TextOrNull("CaPath", null);

        // A CA path is the only transport mode carrying extra data; everything else is the mode name.
        if (caPath is not null)
            return TransportSecurity.Certificate(caPath, reader.TextOrNull("Password", null));

        return reader.Parsed("Mode", fallback, $"{TransportSecurity.NoneText}, {TransportSecurity.StandardText} or {TransportSecurity.InsecureText}");
    }

    static ClientCredentials ReadCredentials(ConfigReader reader, ClientCredentials fallback) {
        if (!reader.Exists) return fallback;

        if (reader.ScalarValue is { } text)
            return ClientCredentials.TryParse(text, null, out var credentials) ? credentials : fallback;

        var certificatePath = reader.TextOrNull("CertificatePath", null);
        var keyPath         = reader.TextOrNull("KeyPath", null);

        if (certificatePath is not null && keyPath is not null)
            return ClientCredentials.Certificate(certificatePath, keyPath, reader.TextOrNull("Password", null));

        var username = reader.TextOrNull("Username", null);

        return username is not null
            ? ClientCredentials.Basic(username, reader.TextOrNull("Password", null))
            : fallback;
    }

    static void ReadResilience(ConfigReader reader, KurrentClientResilienceOptions resilience) {
        if (!reader.Exists) return;

        resilience.KeepAliveInterval = reader.Duration("KeepAliveInterval", resilience.KeepAliveInterval);
        resilience.KeepAliveTimeout  = reader.Duration("KeepAliveTimeout", resilience.KeepAliveTimeout);
        resilience.Deadline          = reader.Duration("Deadline", resilience.Deadline);

        var retry = reader.Child("Retry");

        if (retry.Exists) {
            resilience.Retry.Enabled           = retry.Flag("Enabled", resilience.Retry.Enabled);
            resilience.Retry.MaxAttempts       = retry.Number("MaxAttempts", resilience.Retry.MaxAttempts);
            resilience.Retry.InitialBackoff    = retry.Duration("InitialBackoff", resilience.Retry.InitialBackoff);
            resilience.Retry.MaxBackoff        = retry.Duration("MaxBackoff", resilience.Retry.MaxBackoff);
            resilience.Retry.BackoffMultiplier = retry.Number("BackoffMultiplier", resilience.Retry.BackoffMultiplier);

            retry.ReportNotConfigurable("ShouldRetry");
            retry.ReportUnrecognised("Enabled", "MaxAttempts", "InitialBackoff", "MaxBackoff", "BackoffMultiplier", "ShouldRetry");
        }

        reader.ReportUnrecognised("KeepAliveInterval", "KeepAliveTimeout", "Deadline", "Retry");
    }

    static void ReadSchema(ConfigReader reader, KurrentClientSchemaOptions schema) {
        if (!reader.Exists) return;

        schema.RegistrationMode = reader.Choice("RegistrationMode", schema.RegistrationMode);
        schema.ValidationMode   = reader.Choice("ValidationMode", schema.ValidationMode);

        reader.ReportNotConfigurable("NameStrategy");
        reader.ReportUnrecognised("RegistrationMode", "ValidationMode", "NameStrategy");
    }

    static void ReadCompression(ConfigReader reader, KurrentClientCompressionOptions compression) {
        if (!reader.Exists) return;

        compression.Enabled   = reader.Flag("Enabled", compression.Enabled);
        compression.Algorithm = reader.Choice("Algorithm", compression.Algorithm);

        reader.ReportUnrecognised("Enabled", "Algorithm");
    }

    #endregion // Client

    #region ->> Processor <<-

    /// <summary>
    /// Reads one processor's settings from <c>Kurrent:Surge:Processors:{name}</c>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="name">
    /// The processor's section name. Matched against PascalCase, kebab-case and snake_case spellings.
    /// </param>
    /// <param name="into">Options to fill. Values already set survive when not mentioned.</param>
    public static ConfigurationReadResult<ProcessorOptions> ReadProcessorOptions(
        IConfiguration configuration,
        string name,
        ProcessorOptions? into = null
    ) {
        var options  = into ?? new ProcessorOptions();
        var problems = new List<ConfigurationProblem>();
        var section  = FindProcessorSection(configuration, name);

        if (section is null)
            return new ConfigurationReadResult<ProcessorOptions>(options, problems);

        var reader = new ConfigReader(section, problems);

        options.ProcessorId              = reader.Text("ProcessorId", options.ProcessorId);
        options.DroneId                  = reader.Text("DroneId", options.DroneId);
        options.Debug                    = reader.Flag("Debug", options.Debug);
        options.SkipDecoding             = reader.Flag("SkipDecoding", options.SkipDecoding);
        options.TrackSourceLag           = reader.Flag("TrackSourceLag", options.TrackSourceLag);
        options.SourceLagRefreshInterval = reader.Duration("SourceLagRefreshInterval", options.SourceLagRefreshInterval);

        ReadSubscription(reader.Child("Subscription"), options.Subscription);
        ReadCheckpoint(reader.Child("Checkpoint"), options.Checkpoint);
        ReadBatching(reader.Child("Batching"), options.Batching);
        ReadProcessorResilience(reader.Child("Resilience"), options.Resilience);
        ReadProcessorConnection(reader, options, problems);

        reader.ReportNotConfigurable("Observers");
        reader.ReportNotConfigurable("MeterFactory");
        reader.ReportNotConfigurable("Time");

        reader.ReportUnrecognised(
            "ProcessorId", "DroneId", "Debug", "SkipDecoding", "TrackSourceLag", "SourceLagRefreshInterval",
            "Subscription", "Checkpoint", "Batching", "Resilience", "Connection", "ConnectionString",
            "Observers", "MeterFactory", "Time");

        return new ConfigurationReadResult<ProcessorOptions>(options, problems);
    }

    /// <summary>
    /// Locates a processor's section, accepting PascalCase, kebab-case and snake_case spellings of
    /// its name, since a generated processor id rarely matches hand-written configuration exactly.
    /// </summary>
    static IConfigurationSection? FindProcessorSection(IConfiguration configuration, string name) {
        var processors = configuration.GetSection($"{SectionPath}:{ProcessorsKey}");

        if (!processors.Exists())
            return null;

        foreach (var candidate in processors.GetChildren()) {
            if (Normalise(candidate.Key) == Normalise(name))
                return candidate;
        }

        return null;

        static string Normalise(string value) =>
            value.Replace("-", "").Replace("_", "").ToLowerInvariant();
    }

    static void ReadSubscription(ConfigReader reader, SubscriptionOptions subscription) {
        if (!reader.Exists) return;

        subscription.Filter          = reader.Parsed("Filter", subscription.Filter, "a filter expression");
        subscription.InitialPosition = reader.Choice("InitialPosition", subscription.InitialPosition);

        reader.ReportUnrecognised("Filter", "InitialPosition");
    }

    static void ReadCheckpoint(ConfigReader reader, CheckpointOptions checkpoint) {
        if (!reader.Exists) return;

        checkpoint.AutoTrack      = reader.Flag("AutoTrack", checkpoint.AutoTrack);
        checkpoint.AutoCheckpoint = reader.Flag("AutoCheckpoint", checkpoint.AutoCheckpoint);
        checkpoint.Interval       = reader.Duration("Interval", checkpoint.Interval);
        checkpoint.RetentionCount = reader.Number("RetentionCount", checkpoint.RetentionCount);
        checkpoint.FailurePolicy  = reader.Choice("FailurePolicy", checkpoint.FailurePolicy);

        reader.ReportUnrecognised("AutoTrack", "AutoCheckpoint", "Interval", "RetentionCount", "FailurePolicy");
    }

    static void ReadBatching(ConfigReader reader, BatchOptions batching) {
        if (!reader.Exists) return;

        batching.MaxBatchSize     = reader.Number("MaxBatchSize", batching.MaxBatchSize);
        batching.BatchWindow      = reader.Duration("BatchWindow", batching.BatchWindow);
        batching.BufferMultiplier = reader.Number("BufferMultiplier", batching.BufferMultiplier);

        reader.ReportUnrecognised("MaxBatchSize", "BatchWindow", "BufferMultiplier");
    }

    static void ReadProcessorResilience(ConfigReader reader, ProcessorResilienceOptions resilience) {
        if (!reader.Exists) return;

        resilience.MaxRetryAttempts = reader.Number("MaxRetryAttempts", resilience.MaxRetryAttempts);
        resilience.BaseDelay        = reader.Duration("BaseDelay", resilience.BaseDelay);
        resilience.MaxDelay         = reader.Duration("MaxDelay", resilience.MaxDelay);

        reader.ReportNotConfigurable("ShouldRetry");
        reader.ReportUnrecognised("MaxRetryAttempts", "BaseDelay", "MaxDelay", "ShouldRetry");
    }

    /// <summary>
    /// Reads the connection a processor uses, which may differ from the one the scope shares.
    /// </summary>
    static void ReadProcessorConnection(ConfigReader reader, ProcessorOptions options, List<ConfigurationProblem> problems) {
        if (reader.TextOrNull("ConnectionString", null) is { } connectionString) {
            options.Connection(connectionString);
            return;
        }

        var connection = reader.Child("Connection");

        if (!connection.Exists)
            return;

        var clientOptions = new KurrentClientOptions();

        ReadClientOptions(connection, clientOptions);
        options.Connection(clientOptions);
    }

    #endregion // Processor
}
