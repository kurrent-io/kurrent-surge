using System.Collections.Concurrent;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Client.Schema.Governance;

/// <summary>
/// The real <see cref="ISchemaGovernance"/>: registry RPC, registration race protocol,
/// version caches, and the client's single-flight catalog registration owned directly.
/// </summary>
sealed class SchemaGovernance : ISchemaGovernance {
    // The registry implements no serializer but Json, so catalog schemas register as Json
    // definitions and nothing else.
    const SchemaDataFormat RegistrationDataFormat = SchemaDataFormat.Json;

    SchemaGovernance(
        RegistryClient registry,
        ISchemaExporter exporter,
        IReadOnlyList<MessageCatalog> catalogs,
        KurrentClientSchemaOptions options,
        CancellationToken ct
    ) {
        _registry = registry;
        _exporter = exporter;
        _options  = options;

        _catalogRegistration = options.SchemaRegistryRequired
            ? new(() => EnsureCatalogRegistered(catalogs, ct), LazyThreadSafetyMode.ExecutionAndPublication)
            : new(static () => Task.FromResult(CatalogSchemaMap.Empty));
    }

    readonly RegistryClient              _registry;
    readonly ISchemaExporter             _exporter;
    readonly KurrentClientSchemaOptions  _options;

    readonly Lazy<Task<CatalogSchemaMap>> _catalogRegistration;

    // Values are swapped, never mutated: readers index [^1] off a bare reference with no lock.
    ConcurrentDictionary<Type, SchemaVersionDescriptor[]> Versions { get; } = new();
    
    ConcurrentDictionary<SchemaVersionId, Type> TypeByVersion { get; } = new();

    public static SchemaGovernance Create(
        KurrentClient client, IReadOnlyList<MessageCatalog> catalogs,
        KurrentClientSchemaOptions options, CancellationToken ct = default
    ) => new(new RegistryClient(client), NJsonSchemaExporter.Instance, catalogs, options, ct);

    public Task<CatalogSchemaMap> CatalogRegistration =>
        _catalogRegistration.Value;

    async Task<CatalogSchemaMap> EnsureCatalogRegistered(IEnumerable<MessageCatalog> catalogs, CancellationToken ct) {
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
                    versionId = await EnsureRegistered(entry.Key, entry.Value, ct).ConfigureAwait(false);
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

                results.Add(entry.Value, new() {
                    Schema     = entry.Key,
                    DataFormat = RegistrationDataFormat,
                    VersionId  = versionId
                });
            }
        }

        return failures.Count > 0 
            ? throw new CatalogRegistrationFailedException(failures)
            : results.Build();
    }

    public async ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, CancellationToken ct) {
        if (_options.RegistrationMode is SchemaRegistrationMode.Auto)
            return (await TryRegister(schema, messageType, RegistrationDataFormat, ct).ConfigureAwait(false)).VersionId;

        if (_options.ValidationMode is SchemaValidationMode.Enabled)
            return await EnsureCompatibleByName(schema, messageType, RegistrationDataFormat, ct).ConfigureAwait(false);

        return SchemaVersionId.Unset;
    }

    public async ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, CancellationToken ct) {
        try {
            if (_options.ValidationMode is not SchemaValidationMode.Enabled)
                return SchemaVersionId.Unset;

            if (schemaInfo.HasSchemaId) {
                await EnsureCompatibleByVersion(schemaInfo.VersionId, messageType, schemaInfo.DataFormat, ct).ConfigureAwait(false);

                // The record already carries its version id — nothing to backfill.
                return SchemaVersionId.Unset;
            }

            // Records appended without a version id: compatibility by name, id returned for backfill.
            return await EnsureCompatibleByName(schemaInfo.Schema, messageType, schemaInfo.DataFormat, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not SchemaValidationException and not OperationCanceledException) {
            throw new SchemaCodecDecodingException(schemaInfo.DataFormat, schemaInfo.Schema, ex);
        }
    }

    async ValueTask<SchemaVersionDescriptor> TryRegister(SchemaName schema, Type messageType, SchemaDataFormat dataFormat, CancellationToken ct) {
        if (Versions.TryGetValue(messageType, out var versions))
            return versions[^1];

        try {
            var version = await _registry.GetSchemaVersion(schema, ct).ConfigureAwait(false);
            return Cache(new(version.VersionId, version.VersionNumber));
        }
        catch (SchemaNotFoundException) {
            // Not registered yet — fall through and create it. Only absence lands here; every
            // other failure propagates instead of being mistaken for an unregistered schema.
        }

        var definition = _exporter.Export(messageType, dataFormat);

        try {
            return Cache(await _registry.CreateSchema(schema, definition, dataFormat, ct).ConfigureAwait(false));
        }
        catch (SchemaAlreadyExistsException) {
            // The name is taken. Either another client registered this same type first — likely,
            // since registry reads lag writes by a few hundred milliseconds — or the name maps to
            // a different type. Compatibility separates the two: a mismatch is a real error, not
            // a race.
            return new(await EnsureCompatibleByName(schema, messageType, dataFormat, ct).ConfigureAwait(false), 0);
        }

        SchemaVersionDescriptor Cache(SchemaVersionDescriptor descriptor) {
            if (!Versions.TryAdd(messageType, [descriptor]))
                return Versions[messageType][^1];

            TypeByVersion.TryAdd(descriptor.VersionId, messageType);

            return descriptor;
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
                // AddOrUpdate re-runs this delegate when its CAS loses, so the guard keeps a
                // retry from appending the same version twice.
                static (_, versions, state) => Array.Exists(versions, v => v.VersionId == state.Version.VersionId)
                    ? versions
                    : [.. versions[..^1], state.Version, versions[^1]],
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
