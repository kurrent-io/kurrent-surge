using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Registry.OrderManagement.Events;

namespace Registry.OrderManagement;

[CustomMessageCatalog]
sealed class OrderManagementMessageCatalog() : MessageCatalog(
    "orders", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("order.placed.v1")
        .Map<OrderShipped>("order.shipped.v1"));
