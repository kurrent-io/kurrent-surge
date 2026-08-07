using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Resolves a record's wire schema before serialization — the resolution the serializer performs
/// internally today, extracted and cached. Completed <see cref="RecordSchemaInfo"/> instances are
/// cached per (type, format): the client's registry policy and the per-format registration options
/// are fixed at client construction, so the result is type-stable for the client's lifetime.
/// </summary>
sealed class SchemaResolver {
    readonly ConcurrentDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo> _cache = new();

    readonly Dictionary<SchemaDataFormat, (ISchemaManager Manager, SchemaRegistrationOptions Registration)> _governance;
    readonly SchemaRegistryPolicy                                                                           _policy;

    SchemaResolver(Dictionary<SchemaDataFormat, (ISchemaManager, SchemaRegistrationOptions)> governance, SchemaRegistryPolicy policy) {
        _governance = governance;
        _policy     = policy;
    }

    /// <summary>
    /// Builds a resolver over the SAME manager and options instances the provider's serializers
    /// hold — a second SchemaManager would fork the version cache and re-freeze the catalogs.
    /// </summary>
    public static SchemaResolver From(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy) {
        var governance = new Dictionary<SchemaDataFormat, (ISchemaManager, SchemaRegistrationOptions)>();

        // Discover every registered format instead of a hardcoded list, so a custom serializer
        // (e.g. registered via KurrentClientMessageOptions.UseSerializer) is picked up here too.
        foreach (var format in Enum.GetValues<SchemaDataFormat>()) {
            if (serializerProvider.SupportsDataFormat(format) && serializerProvider.GetSerializer(format) is SchemaSerializerBase serializer)
                governance[format] = (GetSchemaManager(serializer), GetOptions(serializer).SchemaRegistration);
        }

        return new(governance, policy);
    }

    public async ValueTask<RecordSchemaInfo> ResolveWriteSchema(Type messageType, string stream, SchemaDataFormat format, CancellationToken ct) {
        if (_cache.TryGetValue((messageType, format), out var cached))
            return cached;

        var (manager, registration) = _governance[format];

        var policy = _policy
            .Resolve(registration, stream, format)
            .EnsureDataFormatCompliance(format);

        try {
            var (schemaName, versionId) = await manager
                .RegisterOrValidateSchema(messageType, policy, ct)
                .ConfigureAwait(false);

            var info = new RecordSchemaInfo {
                Schema     = schemaName,
                DataFormat = format,
                VersionId  = versionId
            };

            _cache.TryAdd((messageType, format), info);
            return info;
        }
        // Mirrors the serializer base's wrap so the split keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException and not SchemaNameNotMappedException) {
            throw new SchemaSerializationFailedException(format, messageType, ex);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_SchemaManager")]
    static extern ISchemaManager GetSchemaManager(SchemaSerializerBase serializer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Options")]
    static extern SchemaSerializerOptions GetOptions(SchemaSerializerBase serializer);
}
