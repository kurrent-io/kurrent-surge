namespace Kurrent.Projectors.DuckDB.Examples;

// Shared domain for every example in this folder — modelled on the SchemaRegistry projections that run
// in production today, so the shape of the surface can be judged against real usage rather than a toy.

#region ->> Messages <<-

public record SchemaCreated(string SchemaVersionId, string SchemaName, int VersionNumber, byte[] SchemaDefinition);

public record SchemaVersionRegistered(string SchemaVersionId, string SchemaName, int VersionNumber);

public record SchemaVersionDeleted(string SchemaVersionId);

public record SchemaDescriptionUpdated(string SchemaName, string Description);

public record SchemaDeleted(string SchemaName);

#endregion

/// <summary>Document shared by the document-projection examples in this folder.</summary>
public record SchemaVersion {
    public required string VersionId        { get; init; }
    public required string SchemaName       { get; init; }
    public required int    VersionNumber    { get; init; }
    public required byte[] SchemaDefinition { get; init; }
}
