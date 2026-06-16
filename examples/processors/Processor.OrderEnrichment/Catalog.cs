using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Processor.OrderEnrichment.Events;

namespace Processor.OrderEnrichment;

[CustomMessageCatalog]
sealed class OrderEnrichmentMessageCatalog() : MessageCatalog(
    "order-enrichment", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("OrderPlaced")
        .Map<CustomerUpdated>("CustomerUpdated")
        .Map<ProductUpdated>("ProductUpdated"));
