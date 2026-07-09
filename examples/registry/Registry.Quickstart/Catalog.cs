using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema;
using Kurrent.Schema.Catalogs;
using Registry.Quickstart.Events;

namespace Registry.Quickstart;

[CustomMessageCatalog]
sealed class QuickstartMessageCatalog() : MessageCatalog(
    "sensors", alwaysActive: true, catalog => catalog
        .Map<TemperatureReading>("TemperatureReading"));
