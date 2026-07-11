namespace Kurrent.Surge.Client.Registry;

public readonly record struct SchemaVersionDescriptor(SchemaVersionId VersionId, int VersionNumber) {
    public static readonly SchemaVersionDescriptor Unset = new(SchemaVersionId.Unset, 0);

    public bool IsUnset => this == Unset;
}
