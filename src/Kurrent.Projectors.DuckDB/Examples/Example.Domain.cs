namespace Kurrent.Projectors.DuckDB.Examples;


#region ->> Messages <<-

public record SchemaCreated(string SchemaVersionId, string SchemaName, int VersionNumber, byte[] SchemaDefinition);

public record SchemaVersionRegistered(string SchemaVersionId, string SchemaName, int VersionNumber);

public record SchemaVersionDeleted(string SchemaVersionId);

public record SchemaDescriptionUpdated(string SchemaName, string Description);

public record SchemaDeleted(string SchemaName);

#endregion

public record SchemaVersion {
    public required string VersionId        { get; init; }
    public required string SchemaName       { get; init; }
    public required int    VersionNumber    { get; init; }
    public required byte[] SchemaDefinition { get; init; }
}
