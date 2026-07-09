using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

[CustomMessageCatalog]
sealed class FraudDetectionEventCatalog() : MessageCatalog(
    "fraud-detection", alwaysActive: true, catalog => catalog
        .Map<Transaction>("Transaction"));
