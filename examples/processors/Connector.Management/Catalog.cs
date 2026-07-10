using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema;
using Connector.Management.Events;
using Kurrent.Surge.Schema.Catalogs;

namespace Connector.Management;

[CustomMessageCatalog]
sealed class PaymentMessageCatalog() : MessageCatalog(
    "payments", alwaysActive: true, catalog => catalog
        .Map<PaymentPlanCreated>("PaymentPlanCreated")
        .Map<PaymentCollected>("PaymentCollected"));
