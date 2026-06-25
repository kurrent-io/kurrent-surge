using Kurrent.Schema.Catalogs;
using Processor.IoTMonitoring.Events;

namespace Processor.IoTMonitoring;

[CustomMessageCatalog]
sealed class IoTMonitoringEventCatalog() : MessageCatalog(
  "iot-monitoring", alwaysActive: true, catalog => catalog.Map<SensorReading>("SensorReading"));
