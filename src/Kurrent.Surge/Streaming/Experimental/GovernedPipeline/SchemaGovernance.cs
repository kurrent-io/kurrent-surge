using System.Collections.Concurrent;
using System.Collections.Frozen;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// The real <see cref="ISchemaGovernance"/>: registry RPC, registration race protocol, and
/// version caches owned directly — no <c>SchemaManager</c> underneath. Registration and
/// validation gate on Json because the server implements no other serializer yet.
/// </summary>
sealed class SchemaGovernance(RegistryClient registry, ISchemaExporter exporter) : ISchemaGovernance {
    ConcurrentDictionary<Type, List<SchemaVersionDescriptor>> Versions { get; } = new();

    // Reverse index for the read path's version-id lookups — replaces the manager's linear
    // scan over every cached type.
    ConcurrentDictionary<SchemaVersionId, Type> TypeByVersion { get; } = new();

    /// <summary>Builds governance over a client's registry module and the default exporter.</summary>
    public static SchemaGovernance Create(KurrentClient client) =>
        new(new RegistryClient(client), NJsonSchemaExporter.Instance);

    public async Task<IReadOnlyDictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>> ValidateCatalog(
        IEnumerable<MessageCatalog> catalogs, ResolvedSchemaRegistryPolicy policy, CancellationToken ct
    ) {
        var results = new Dictionary<(Type MessageType, SchemaDataFormat Format), RecordSchemaInfo>();

        foreach (var catalog in catalogs) {
            foreach (var entry in catalog.Entries) {
                var versionId = await EnsureRegistered(entry.Key, entry.Value, policy, ct).ConfigureAwait(false);

                results[(entry.Value, policy.DataFormat)] = new RecordSchemaInfo {
                    Schema     = entry.Key,
                    DataFormat = policy.DataFormat,
                    VersionId  = versionId
                };
            }
        }

        return results.ToFrozenDictionary();
    }

    public async ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        try {
            if (policy is { AutoRegisterSchemas: true, DataFormat: SchemaDataFormat.Json })
                return (await TryRegister(schema, messageType, policy.DataFormat, ct).ConfigureAwait(false)).VersionId;

            if (policy is { ValidateSchemas: true, DataFormat: SchemaDataFormat.Json })
                return await EnsureCompatibleByName(schema, messageType, policy.DataFormat, ct).ConfigureAwait(false);

            return SchemaVersionId.Unset;
        }
        // Mirrors the serializer base's wrap so the governed chain keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaSerializationFailedException(policy.DataFormat, messageType, ex);
        }
    }

    public async ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        try {
            if (!policy.ValidateSchemas)
                return SchemaVersionId.Unset;

            if (schemaInfo.HasSchemaId) {
                await EnsureCompatibleByVersion(schemaInfo.VersionId, messageType, schemaInfo.DataFormat, ct).ConfigureAwait(false);

                // The record already carries its version id — nothing to backfill.
                return SchemaVersionId.Unset;
            }

            // Records appended without a version id: compatibility by name, id returned for backfill.
            return await EnsureCompatibleByName(schemaInfo.Schema, messageType, schemaInfo.DataFormat, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaDeserializationFailedException(schemaInfo.DataFormat, schemaInfo.Schema, ex);
        }
    }

    async ValueTask<SchemaVersionDescriptor> TryRegister(SchemaName schema, Type messageType, SchemaDataFormat dataFormat, CancellationToken ct) {
        if (Versions.TryGetValue(messageType, out var versions))
            return versions[^1];

        try {
            var version    = await registry.GetSchemaVersion(schema, ct).ConfigureAwait(false);
            var descriptor = new SchemaVersionDescriptor(version.VersionId, version.VersionNumber);

            if (Versions.TryAdd(messageType, [descriptor]))
                TypeByVersion.TryAdd(descriptor.VersionId, messageType);

            return descriptor;
        }
        catch (Exception) when (!ct.IsCancellationRequested) {
            // schema version not found, proceed to create
        }

        var definition = exporter.Export(messageType, dataFormat);

        try {
            var descriptor = await registry.CreateSchema(schema, definition, dataFormat, ct).ConfigureAwait(false);

            // edge case: the schema was created by another client between checking and creating
            if (Versions.TryAdd(messageType, [descriptor])) {
                TypeByVersion.TryAdd(descriptor.VersionId, messageType);
                return descriptor;
            }

            return await TryRegister(schema, messageType, dataFormat, ct).ConfigureAwait(false);
        }
        catch (SchemaAlreadyExistsException) {
            // edge case: the schema was created by another client between checking and creating
            return await TryRegister(schema, messageType, dataFormat, ct).ConfigureAwait(false);
        }
    }

    async ValueTask<SchemaVersionId> EnsureCompatibleByVersion(SchemaVersionId versionId, Type messageType, SchemaDataFormat dataFormat, CancellationToken ct) {
        if (TypeByVersion.TryGetValue(versionId, out var knownType) && Versions.TryGetValue(knownType, out var known))
            return known[^1].VersionId;

        var definition = exporter.Export(messageType, dataFormat);

        try {
            var lastVersionId = await registry.CheckSchemaCompatibility(versionId, definition, dataFormat, ct).ConfigureAwait(false);

            // The last element stays the latest known version; the checked version slots in
            // before it.
            Versions.AddOrUpdate(
                messageType,
                static (_, state) => [state.Version, state.Last],
                static (_, versions, state) => {
                    versions.Insert(versions.Count - 1, state.Version);
                    return versions;
                },
                (Last: new SchemaVersionDescriptor(lastVersionId, 0), Version: new SchemaVersionDescriptor(versionId, 0))
            );

            TypeByVersion.TryAdd(versionId, messageType);
            TypeByVersion.TryAdd(lastVersionId, messageType);

            return lastVersionId;
        }
        catch (SchemaIncompatibleException incompatible) {
            throw new SchemaValidationException(dataFormat, versionId, messageType, incompatible.Errors.Errors);
        }
    }

    async ValueTask<SchemaVersionId> EnsureCompatibleByName(SchemaName schema, Type messageType, SchemaDataFormat dataFormat, CancellationToken ct) {
        if (Versions.TryGetValue(messageType, out var versions))
            return versions[^1].VersionId;

        var definition = exporter.Export(messageType, dataFormat);

        try {
            var versionId = await registry.CheckSchemaCompatibility(schema, definition, dataFormat, ct).ConfigureAwait(false);

            if (Versions.TryAdd(messageType, [new(versionId, 0)]))
                TypeByVersion.TryAdd(versionId, messageType);

            return versionId;
        }
        catch (SchemaIncompatibleException incompatible) {
            throw new SchemaValidationException(dataFormat, schema, messageType, incompatible.Errors.Errors);
        }
    }
}
