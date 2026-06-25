using Kurrent.Schema.Catalogs;
using Processor.OrderEnrichment.Events;

namespace Processor.OrderEnrichment;

[CustomMessageCatalog]
sealed class OrderEnrichmentEventCatalog() : MessageCatalog(
    "order-enrichment", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>()
        .Map<CustomerUpdated>()
        .Map<ProductUpdated>());
