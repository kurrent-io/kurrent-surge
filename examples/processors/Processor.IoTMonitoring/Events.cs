namespace Processor.IoTMonitoring.Events;

/// <summary>
/// Event representing a sensor reading from an IoT device.
/// </summary>
/// <param name="SensorId">Unique identifier for the sensor.</param>
/// <param name="DeviceId">Identifier for the device containing this sensor.</param>
/// <param name="Temperature">Temperature reading in Celsius.</param>
/// <param name="Humidity">Humidity reading as percentage (0-100).</param>
/// <param name="Location">Physical location of the sensor.</param>
/// <param name="Timestamp">When the reading was taken.</param>
public record SensorReading(
    string SensorId,
    string DeviceId,
    double Temperature,
    double Humidity,
    string Location,
    DateTimeOffset Timestamp
);