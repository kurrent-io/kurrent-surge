using Kurrent.Client;
using Kurrent.Client.Schema;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

[CustomMessageCatalog]
sealed class FraudMessageCatalog() : MessageCatalog(
    "fraud", alwaysActive: true, catalog => catalog
        .Map<Transaction>("Transaction"));
