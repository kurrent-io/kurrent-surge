namespace Registry.Quickstart;

public record TemperatureReading(string SensorId, double Celsius, DateTimeOffset Timestamp, string? Location = null);
