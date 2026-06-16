using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Processor.ClickstreamSessions.Events;

namespace Processor.ClickstreamSessions;

[CustomMessageCatalog]
sealed class ClickstreamMessageCatalog() : MessageCatalog(
    "clickstream", alwaysActive: true, catalog => catalog
        .Map<PageView>("PageView"));
