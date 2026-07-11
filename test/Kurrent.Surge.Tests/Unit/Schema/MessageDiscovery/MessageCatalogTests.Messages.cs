using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Tests.Schema.Catalogs.Filtered;

// Exercises the MessageCatalogGenerator against this assembly: the generator emits the
// MessageCatalogs class from these attributes, and MessageCatalogTests asserts on it.
[assembly: MessageCatalog("Kurrent.Surge.Tests.Schema.Catalogs.Scanned", Key = "scanned")]
[assembly: MessageCatalog<ICatalogMarker>("Kurrent.Surge.Tests.Schema.Catalogs.Filtered", Key = "filtered")]

// Assembly-level explicit registration — the "types you don't own" form; no Catalog key → "Default".
[assembly: MessageType<Kurrent.Surge.Tests.Schema.Catalogs.External.ExternalInvoiceIssued>("external.invoice-issued.v1")]

// Always-active scan: registers into every client with no UseCatalog call.
[assembly: MessageCatalog("Kurrent.Surge.Tests.Schema.Catalogs.Ambient", Key = "ambient", AlwaysActive = true)]

namespace Kurrent.Surge.Tests.Schema.Catalogs.Scanned {
    public record ScannedOrderPlaced(string OrderId);

    public record ScannedOrderShipped(string OrderId);

    // None of the following should be discovered:
    public abstract class AbstractMessage;

    public static class StaticHolder;

    public record OpenGenericMessage<T>(T Value);
}

namespace Kurrent.Surge.Tests.Schema.Catalogs.Filtered {
    public interface ICatalogMarker;

    public record MarkedMessage(string Id) : ICatalogMarker;

    // Not discovered: does not implement ICatalogMarker.
    public record UnmarkedMessage(string Id);
}

namespace Kurrent.Surge.Tests.Schema.Catalogs.Explicit {
    // Pin form joining a named catalog.
    [MessageType<PinnedOrderPlaced>("pinned.order-placed.v9", Catalog = "pinned")]
    public record PinnedOrderPlaced(string Id);

    // Strategy form: naming is compile-checked via the generic constraint.
    [MessageType<KebabOrderArchived, KebabNamesStrategy>(Catalog = "pinned")]
    public record KebabOrderArchived(string Id);

    public sealed class KebabNamesStrategy() :
        Kurrent.Surge.Schema.Naming.MessageSchemaNameStrategy(Kurrent.Surge.Schema.Naming.SchemaNameOutputFormat.KebabCase);
}

namespace Kurrent.Surge.Tests.Schema.Catalogs.External {
    // Registered via the assembly-level [MessageType<T>] above — no scan covers this
    // namespace and the type carries no attribute, simulating a referenced contract.
    public record ExternalInvoiceIssued(string Id);
}

namespace Kurrent.Surge.Tests.Schema.Catalogs.Ambient {
    // Scanned into the always-active "ambient" catalog above.
    public record AmbientSignal(string Id);
}

namespace Kurrent.Surge.Tests.Schema.Catalogs.Custom {
    public record HandRolledPing(string Id);

    // Hand-authored catalog: discovery, the MessageCatalogs.HandRolled property, and the
    // UseHandRolledCatalog() extension all come from [CustomMessageCatalog] alone.
    [CustomMessageCatalog]
    sealed class HandRolledCatalog() : MessageCatalog("hand-rolled", c => c
        .Map<HandRolledPing>("hand-rolled.ping.v1"));
}
