using System.Collections.Concurrent;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Client.Schema.Governance;

/// <summary>
/// The real <see cref="ISchemaGovernance"/>: registry RPC, registration race protocol,
/// version caches, and the client's single-flight catalog registration owned directly.
/// Registration and validation gate on Json because the server implements no other
/// serializer yet.
/// </summary>
sealed class SchemaGovernance : ISchemaGovernance {
    SchemaGovernance(
        RegistryClient registry, ISchemaExporter exporter, IReadOnlyList<MessageCatalog> catalogs,
        ResolvedSchemaRegistryPolicy policy, CancellationToken lifetime
    ) {
        _registry = registry;
        _exporter = exporter;

        _catalogRegistration = policy.SchemaRegistryRequired
            ? new(() => EnsureCatalogRegistered(catalogs, policy, lifetime), LazyThreadSafetyMode.ExecutionAndPublication)
            : new(static () => Task.FromResult(CatalogSchemaMap.Empty));
    }

    readonly RegistryClient  _registry;
    readonly ISchemaExporter _exporter;

    readonly Lazy<Task<CatalogSchemaMap>> _catalogRegistration;

    ConcurrentDictionary<Type, List<SchemaVersionDescriptor>> Versions { get; } = new();

    // Reverse index for the read path's version-id lookups — replaces the manager's linear
    // scan over every cached type.
    ConcurrentDictionary<SchemaVersionId, Type> TypeByVersion { get; } = new();

    /// <summary>
    /// Builds governance over a client's registry module and the default exporter, with the
    /// catalog registration bound to the given catalogs, resolved policy, and lifetime.
    /// </summary>
    public static SchemaGovernance Create(
        KurrentClient client, IReadOnlyList<MessageCatalog> catalogs,
        ResolvedSchemaRegistryPolicy policy, CancellationToken lifetime = default
    ) =>
        new(new RegistryClient(client), NJsonSchemaExporter.Instance, catalogs, policy, lifetime);

    public Task<CatalogSchemaMap> CatalogRegistration =>
        _catalogRegistration.Value;

    public void StartCatalogRegistration() =>
        // A fault stays stored in the task for awaiters; touching the exception here keeps
        // a never-awaited failure off the unobserved-exception path.
        _ = CatalogRegistration.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );

    async Task<CatalogSchemaMap> EnsureCatalogRegistered(
        IEnumerable<MessageCatalog> catalogs, ResolvedSchemaRegistryPolicy policy, CancellationToken ct
    ) {
        var results  = new CatalogSchemaMap.Builder();
        var failures = new List<CatalogRegistrationFailure>();

        foreach (var catalog in catalogs) {
            foreach (var entry in catalog.Entries) {
                // System vocabulary ($>, $metadata, …) is the server's, never client-registered —
                // the registry's name validator rejects those names outright.
                if (entry.Key.StartsWith('$'))
                    continue;

                SchemaVersionId versionId;
                try {
                    versionId = await EnsureRegistered(entry.Key, entry.Value, policy, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    throw;
                }
                catch (Exception ex) {
                    // The walk covers every entry before reporting, so one restart fixes
                    // the whole catalog instead of one schema per restart.
                    failures.Add(new(entry.Key, entry.Value, ex));
                    continue;
                }

                results.Add(entry.Value, policy.DataFormat, new RecordSchemaInfo {
                    Schema     = entry.Key,
                    DataFormat = policy.DataFormat,
                    VersionId  = versionId
                });
            }
        }

        if (failures.Count > 0)
            throw new CatalogRegistrationFailedException(failures);

        return results.Build();
    }

    public async ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct) {
        if (policy is { AutoRegisterSchemas: true, DataFormat: SchemaDataFormat.Json })
            return (await TryRegister(schema, messageType, policy.DataFormat, ct).ConfigureAwait(false)).VersionId;

        if (policy is { ValidateSchemas: true, DataFormat: SchemaDataFormat.Json })
            return await EnsureCompatibleByName(schema, messageType, policy.DataFormat, ct).ConfigureAwait(false);

        return SchemaVersionId.Unset;
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
            var version    = await _registry.GetSchemaVersion(schema, ct).ConfigureAwait(false);
            var descriptor = new SchemaVersionDescriptor(version.VersionId, version.VersionNumber);

            if (Versions.TryAdd(messageType, [descriptor]))
                TypeByVersion.TryAdd(descriptor.VersionId, messageType);

            return descriptor;
        }
        catch (Exception) when (!ct.IsCancellationRequested) {
            // schema version not found, proceed to create
        }

        var definition = _exporter.Export(messageType, dataFormat);

        try {
            var descriptor = await _registry.CreateSchema(schema, definition, dataFormat, ct).ConfigureAwait(false);

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

        var definition = _exporter.Export(messageType, dataFormat);

        try {
            var lastVersionId = await _registry.CheckSchemaCompatibility(versionId, definition, dataFormat, ct).ConfigureAwait(false);

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

        var definition = _exporter.Export(messageType, dataFormat);

        try {
            var versionId = await _registry.CheckSchemaCompatibility(schema, definition, dataFormat, ct).ConfigureAwait(false);

            if (Versions.TryAdd(messageType, [new(versionId, 0)]))
                TypeByVersion.TryAdd(versionId, messageType);

            return versionId;
        }
        catch (SchemaIncompatibleException incompatible) {
            throw new SchemaValidationException(dataFormat, schema, messageType, incompatible.Errors.Errors);
        }
    }
}
