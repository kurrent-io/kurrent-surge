using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Fallback <see cref="ISchemaGovernance"/> over the client's live <see cref="ISchemaManager"/>.
/// The manager re-runs its own catalog lookups internally — redundant here, tolerated so this
/// adapter shares the manager's version cache and catalog snapshot with the production paths.
/// <see cref="SchemaGovernance"/> is the real implementation.
/// </summary>
sealed class SchemaManagerGovernanceAdapter(ISchemaManager manager) : ISchemaGovernance {
    // Same lifetime caveat as SchemaResolver's cache: safe while the client's registry policy
    // is fixed at construction (NoRequirements today) — a policy that varies per stream needs
    // the resolved policy in the key.
    readonly ConcurrentDictionary<(Type MessageType, SchemaDataFormat Format), SchemaVersionId> _registered = new();

    public async ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        if (_registered.TryGetValue((messageType, policy.DataFormat), out var cached))
            return cached;

        try {
            var (_, versionId) = await manager
                .RegisterOrValidateSchema(messageType, policy, ct)
                .ConfigureAwait(false);

            _registered.TryAdd((messageType, policy.DataFormat), versionId);
            return versionId;
        }
        // Mirrors the serializer base's wrap so the governed chain keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException and not SchemaNameNotMappedException) {
            throw new SchemaSerializationFailedException(policy.DataFormat, messageType, ex);
        }
    }

    public async ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        try {
            var (_, versionId) = await manager
                .ValidateAndEnsureSchemaCompatibility(schemaInfo, policy, ct)
                .ConfigureAwait(false);

            return versionId;
        }
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaDeserializationFailedException(schemaInfo.DataFormat, schemaInfo.Schema, ex);
        }
    }

    public async Task ValidateCatalog(IEnumerable<MessageCatalog> catalogs, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        foreach (var catalog in catalogs)
            foreach (var entry in catalog.Entries)
                await EnsureRegistered(entry.Key, entry.Value, policy, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Harvests the pipeline components from the SAME instances the provider's serializers
    /// hold — a second SchemaManager would fork the version cache and re-freeze the catalogs.
    /// Base-derived serializers only, like the split; Bytes branches around governance entirely.
    /// </summary>
    public static (SchemaManager Manager, MessageResolver Messages, IReadOnlyDictionary<SchemaDataFormat, SchemaRegistrationOptions> Registrations) Extract(
        ISchemaSerializerProvider serializerProvider
    ) {
        ISchemaManager? manager = null;

        var registrations = new Dictionary<SchemaDataFormat, SchemaRegistrationOptions>();

        foreach (var format in Enum.GetValues<SchemaDataFormat>()) {
            if (serializerProvider.SupportsDataFormat(format) && serializerProvider.GetSerializer(format) is SchemaSerializerBase serializer) {
                manager              ??= GetSchemaManager(serializer);
                registrations[format]  = GetOptions(serializer).SchemaRegistration;
            }
        }

        if (manager is not SchemaManager concrete)
            throw new InvalidOperationException("No SchemaManager-backed serializer registered; the governed pipeline experiment requires one.");

        return (concrete, GetResolver(concrete), registrations);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_SchemaManager")]
    static extern ISchemaManager GetSchemaManager(SchemaSerializerBase serializer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Options")]
    static extern SchemaSerializerOptions GetOptions(SchemaSerializerBase serializer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Resolver")]
    static extern MessageResolver GetResolver(SchemaManager manager);
}
