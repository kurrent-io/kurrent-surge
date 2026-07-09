using Kurrent.Surge.Client.Registry;
using Kurrent.Schema;

namespace Registry.Quickstart;

public static class Schemas {
    public static readonly SchemaName TemperatureReading = SchemaName.From("TemperatureReading");

    public const string V1 = """
        {
          "type": "object",
          "required": ["sensorId", "celsius", "timestamp"],
          "properties": {
            "sensorId":  { "type": "string" },
            "celsius":   { "type": "number" },
            "timestamp": { "type": "string", "format": "date-time" }
          }
        }
        """;

    public const string V2WithLocation = """
        {
          "type": "object",
          "required": ["sensorId", "celsius", "timestamp"],
          "properties": {
            "sensorId":  { "type": "string" },
            "celsius":   { "type": "number" },
            "timestamp": { "type": "string", "format": "date-time" },
            "location":  { "type": "string" }
          }
        }
        """;

    public const string BreakingRename = """
        {
          "type": "object",
          "required": ["sensorId", "tempC", "timestamp"],
          "properties": {
            "sensorId":  { "type": "string" },
            "tempC":     { "type": "number" },
            "timestamp": { "type": "string", "format": "date-time" }
          }
        }
        """;
}
