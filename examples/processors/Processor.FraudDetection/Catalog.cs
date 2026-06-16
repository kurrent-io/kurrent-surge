using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

[CustomMessageCatalog]
sealed class FraudMessageCatalog() : MessageCatalog(
    "fraud", alwaysActive: true, catalog => catalog
        .Map<Transaction>("Transaction"));
