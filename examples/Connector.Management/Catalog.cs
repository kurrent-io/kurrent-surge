using Kurrent.Client;
using Kurrent.Client.Schema;
using Connector.Management.Events;
using Kurrent.Schema.Catalogs;

namespace Connector.Management;

[CustomMessageCatalog]
sealed class PaymentMessageCatalog() : MessageCatalog(
    "payments", alwaysActive: true, catalog => catalog
        .Map<PaymentPlanCreated>("PaymentPlanCreated")
        .Map<PaymentCollected>("PaymentCollected"));
