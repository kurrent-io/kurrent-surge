using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Processor.IoTMonitoring.Events;

namespace Processor.IoTMonitoring;

sealed class SensorReadingSeeder(KurrentClient client, ILogger<SensorReadingSeeder> logger) : BackgroundService {
    static readonly (string SensorId, string DeviceId, string Location, double Baseline)[] Sensors = [
        ("temp-001", "device-a", "Server Room A", 24.0),
        ("temp-002", "device-b", "Warehouse Floor", 18.0),
        ("temp-003", "device-c", "Loading Dock", 15.0)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // The processor subscribes from the latest position; give it a moment to attach.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var i = 0;

        while (!stoppingToken.IsCancellationRequested) {
            var sensor = Sensors[i % Sensors.Length];
            var stream = $"sensor-{sensor.SensorId}";
            var round  = i / Sensors.Length;

            // Steady readings within a degree of the baseline. Every tenth round spikes or drops,
            // so both the threshold alerts and the anomaly detector fire.
            var temperature = round % 10 == 9
                ? round % 20 == 9 ? 85.2 : 5.0
                : sensor.Baseline + (Random.Shared.NextDouble() - 0.5) * 2;

            var reading = new SensorReading(
                sensor.SensorId, sensor.DeviceId, Math.Round(temperature, 1),
                Math.Round(40 + Random.Shared.NextDouble() * 20, 1), sensor.Location, DateTimeOffset.UtcNow);

            await client.Streams.AppendStream(stream, [AppendRecord.Create(reading, r => r.Stream = stream)], stoppingToken);

            logger.LogInformation("[seed] {SensorId} {Temp:F1}°C", reading.SensorId, reading.Temperature);

            i++;

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }
}
