using Kurrent.Client;
using Kurrent.Client.Streams;
using Serilog;

namespace Registry.Quickstart;

public class DashboardService(KurrentClient client) {
    public async Task ShowReadings(string stream) {
        await foreach (var msg in client.Streams.ReadStreamRecords(stream)) {
            msg.Switch(
                record => {
                    var r = record.As<TemperatureReading>();
                    var loc = r.Location is not null ? $" ({r.Location})" : "";
                    Log.Information("[dashboard] {SensorId}: {Celsius} C{Location}",
                        r.SensorId, r.Celsius, loc);
                },
                _ => { });
        }
    }
}
