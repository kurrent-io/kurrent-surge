namespace Kurrent.Surge.Schema.Catalogs;

/// <summary>
/// The assembly's catalog export manifest. The message catalog source generator derives this
/// attribute per assembly and applies it at assembly level; at client construction the SDK
/// enumerates loaded assemblies' provider attributes and collects their catalogs.
/// </summary>
/// <remarks>
/// The derived-attribute shape is deliberate: catalogs are handed over through an ordinary
/// virtual call — no member reflection, no <c>DynamicallyAccessedMembers</c> annotations —
/// and the attribute application itself roots the generated catalog class (and everything it
/// references) against trimming, making auto-discovery fully NativeAOT-compatible.
/// Users never write or read this attribute; it is generated.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public abstract class MessageCatalogProviderAttribute : Attribute {
    public abstract IReadOnlyList<MessageCatalog> GetCatalogs();
}
