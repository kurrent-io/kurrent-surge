using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Serilog;

namespace Registry.Quickstart;

public class IngestionService(KurrentClient client) {
    public async Task WriteReadings(string stream, params TemperatureReading[] readings) {
        var records = readings
            .Select(r => AppendRecord.New.WithValue(r).Build())
            .ToArray();

        await client.Streams.AppendStream(stream, records);

        Log.Information("[ingestion] Wrote {Count} readings to {Stream}", records.Length, stream);
    }

    public async Task<bool> CheckCompatibility(string definition) {
        try {
            await client.Registry.CheckSchemaCompatibility(
                Schemas.TemperatureReading, definition, SchemaDataFormat.Json);

            return true;
        }
        catch (SchemaIncompatibleException ex) {
            Log.Warning("[ingestion] Schema change blocked — {Error}",
                string.Join("; ", ex.Errors.Select(e => $"{e.Kind} at {e.PropertyPath}")));
            return false;
        }
    }

    public async Task EvolveSchema(string definition) {
        try {
            var version = await client.Registry.RegisterSchemaVersion(
                Schemas.TemperatureReading, definition);

            Log.Information("[ingestion] Evolved schema to version {Version}", version.VersionNumber);
        }
        catch (KurrentException) {
            Log.Information("[ingestion] Schema already evolved");
        }
    }
}
