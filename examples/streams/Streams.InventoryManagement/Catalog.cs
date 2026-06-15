using Kurrent.Client;
using Kurrent.Client.Schema;
using Streams.InventoryManagement.Events;

namespace Streams.InventoryManagement;

[CustomMessageCatalog]
sealed class InventoryMessageCatalog() : MessageCatalog(
    "inventory", alwaysActive: true, catalog => catalog
        .Map<StockReceived>("StockReceived")
        .Map<StockShipped>("StockShipped")
        .Map<StockAdjusted>("StockAdjusted")
        .Map<StockTransferred>("StockTransferred"));
