namespace Registry.Quickstart.Events;

public record TemperatureReading(string SensorId, double Celsius, DateTimeOffset Timestamp, string? Location = null);
