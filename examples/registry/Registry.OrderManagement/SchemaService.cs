using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Registry.OrderManagement;

public sealed class SchemaService(RegistryClient registry, ILogger<SchemaService> logger) {
    public async Task<SchemaVersionDescriptor> Register(
        SchemaName schemaName, string definition, string description,
        CompatibilityMode compatibility, string domain, string team
    ) {
        var result = await registry.CreateSchema(
            CreateSchemaRequest.For(schemaName)
                .WithDefinition(definition)
                .WithDataFormat(SchemaDataFormat.Json)
                .WithCompatibility(compatibility)
                .WithDescription(description)
                .WithTag("domain", domain)
                .WithTag("team", team)
                .Build());

        logger.LogSchemaRegistered(schemaName, result.VersionNumber, result.VersionId);
        return result;
    }

    public async Task<SchemaVersionDescriptor> Evolve(SchemaName schemaName, string definition) {
        var result = await registry.RegisterSchemaVersion(schemaName, definition);
        logger.LogSchemaEvolved(schemaName, result.VersionNumber, result.VersionId);
        return result;
    }

    /// <summary>
    /// Dry-run a proposed change. Returns compatibility errors instead of throwing
    /// so callers can inspect what broke.
    /// </summary>
    public async Task<CompatibilityResult> CheckCompatibility(
        SchemaName schemaName, string proposedDefinition
    ) {
        try {
            await registry.CheckSchemaCompatibility(
                schemaName, proposedDefinition, SchemaDataFormat.Json);
            return new CompatibilityResult(true, []);
        }
        catch (SchemaIncompatibleException ex) {
            var errors = ex.Errors
                .Select(e => new CompatibilityError(e.Kind.ToString(), e.PropertyPath, e.Details))
                .ToList();
            logger.LogSchemaIncompatible(schemaName, errors.Count);
            return new CompatibilityResult(false, errors);
        }
    }

    public async Task<Schema> GetInfo(SchemaName schemaName) =>
        await registry.GetSchema(schemaName);

    public async Task<SchemaVersion> GetVersion(SchemaName schemaName, int versionNumber) =>
        await registry.GetSchemaVersion(schemaName, versionNumber);

    public async Task<SchemaVersion> GetLatestVersion(SchemaName schemaName) =>
        await registry.GetSchemaVersion(schemaName);

    public async Task<SchemaVersion> ResolveVersion(SchemaVersionId versionId) =>
        await registry.GetSchemaVersionById(versionId);

    public async Task<IReadOnlyList<Schema>> Discover(string? prefix = null, string? team = null) {
        var builder = ListSchemasRequest.New;
        if (prefix is not null) builder = builder.WithPrefix(prefix);
        if (team is not null)   builder = builder.WithTag("team", team);
        return await registry.ListSchemas(builder.Build());
    }

    public async Task Delete(SchemaName schemaName) {
        await registry.DeleteSchema(schemaName);
        logger.LogSchemaDeleted(schemaName);
    }
}

public record CompatibilityResult(bool IsCompatible, List<CompatibilityError> Errors);
public record CompatibilityError(string Kind, string PropertyPath, string Details);

static partial class SchemaServiceLogExtensions {
    [LoggerMessage(Level = LogLevel.Information, Message = "Registered {Schema} v{Version} (id: {Id})")]
    public static partial void LogSchemaRegistered(this ILogger logger, SchemaName schema, int version, SchemaVersionId id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Evolved {Schema} to v{Version} (id: {Id})")]
    public static partial void LogSchemaEvolved(this ILogger logger, SchemaName schema, int version, SchemaVersionId id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Schema change rejected for {Schema} ({Count} error(s))")]
    public static partial void LogSchemaIncompatible(this ILogger logger, SchemaName schema, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Schema}")]
    public static partial void LogSchemaDeleted(this ILogger logger, SchemaName schema);
}
