using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

[CustomMessageCatalog]
sealed class FraudDetectionEventCatalog() : MessageCatalog(
    "fraud-detection", alwaysActive: true, catalog => catalog
        .Map<Transaction>("Transaction"));
