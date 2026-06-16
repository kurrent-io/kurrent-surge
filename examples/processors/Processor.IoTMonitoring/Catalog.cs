using Kurrent.Client;
using Kurrent.Client.Schema;
using Kurrent.Schema.Catalogs;
using Processor.IoTMonitoring.Events;

namespace Processor.IoTMonitoring;

[CustomMessageCatalog]
sealed class IoTMessageCatalog() : MessageCatalog(
  "iot", alwaysActive: true, catalog => catalog.Map<SensorReading>("SensorReading"));
