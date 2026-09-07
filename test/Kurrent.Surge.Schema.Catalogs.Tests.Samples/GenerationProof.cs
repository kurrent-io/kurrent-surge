namespace Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples;

/// <summary>
/// Compile-time proof that the catalog generator ran in this contracts-only assembly — it references
/// ONLY Kurrent.Surge.Core, no Kurrent.Surge. Referencing the generated <c>MessageCatalogs</c>
/// aggregate fails the build if generation was skipped.
/// The Kurrent.Surge.Schema.Catalogs.Generators.Tests suite reads these at runtime.
/// </summary>
public static class GenerationProof {
    public static IReadOnlyList<MessageCatalog> All => MessageCatalogs.All;

    /// <summary>Group-scan catalog over the Climate namespace.</summary>
    public static MessageCatalog Climate => MessageCatalogs.Climate;

    /// <summary>Hand-written <c>[CustomMessageCatalog]</c> over the Security events.</summary>
    public static MessageCatalog Security => MessageCatalogs.Security;

    /// <summary>Default catalog — holds the pinned per-type <c>[MessageType]</c> declaration.</summary>
    public static MessageCatalog Default => MessageCatalogs.Default;
}
