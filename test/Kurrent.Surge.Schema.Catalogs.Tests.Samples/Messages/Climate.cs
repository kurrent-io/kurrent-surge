namespace Kurrent.Surge.Schema.Catalogs.Tests.Samples.SmartHome.Climate;

/// <summary>A thermostat or sensor reported the ambient temperature in a room.</summary>
public record TemperatureMeasured(string DeviceId, double Celsius, DateTimeOffset MeasuredAt);

/// <summary>A sensor reported the relative humidity (0–100) in a room.</summary>
public record HumidityMeasured(string DeviceId, double RelativeHumidity, DateTimeOffset MeasuredAt);

/// <summary>A target temperature was set on a thermostat, attributed to whoever changed it.</summary>
public record ThermostatSetpointChanged(string DeviceId, double TargetCelsius, string ChangedBy);
