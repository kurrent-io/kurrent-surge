namespace Kurrent.Schema.Catalogs.Tests.Samples;

/// <summary>
/// Compile-time proof that the catalog generator ran in this contracts-only assembly — it references
/// ONLY Kurrent.Schema.Catalogs, no Kurrent.Client. Referencing the generated <c>MessageCatalogs</c>
/// aggregate fails the build if generation was skipped, or if the emission gate wrongly produced the
/// client-coupled Use*Catalog extensions (which reference the absent KurrentClientMessageOptions).
/// The Kurrent.Schema.Catalogs.Tests suite reads these at runtime.
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
