using Kurrent.Surge.Schema.Catalogs;
using Processor.OrderEnrichment.Events;

namespace Processor.OrderEnrichment;

[CustomMessageCatalog]
sealed class OrderEnrichmentEventCatalog() : MessageCatalog(
    "order-enrichment", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("OrderPlaced")
        .Map<CustomerUpdated>("CustomerUpdated")
        .Map<ProductUpdated>("ProductUpdated"));
