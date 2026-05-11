namespace Kurrent.Client.Schema;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MessageDiscoveryAttribute(string NamespacePrefix) : Attribute {
    // Creating a hard reference to this type forces the assembly to load/stay
    public Type? AssemblyAnchor { get; init; }

    /// <summary>
    /// If specified, only messages that implement this type will be discovered.
    /// </summary>
    public Type? MustImplement { get; init; }
}