using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Registry.OrderManagement.Events;

namespace Registry.OrderManagement;

[CustomMessageCatalog]
sealed class OrderManagementMessageCatalog() : MessageCatalog(
    "orders", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("order.placed.v1")
        .Map<OrderShipped>("order.shipped.v1"));
