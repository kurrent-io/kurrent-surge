using System.Collections.Concurrent;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.IoTMonitoring.Events;

namespace Processor.IoTMonitoring;

/// <summary>
/// IoT sensor monitoring processor with threshold alerts and anomaly detection.
/// Demonstrates tumbling windows and real-time alerting patterns.
/// </summary>
public sealed class TemperatureMonitorProcessor : IRecordProcessor {
    const double HighTempThreshold = 80.0;  // Celsius
    const double LowTempThreshold = 10.0;   // Celsius
    const int WindowSize = 10;               // Number of readings for rolling window
    const double AnomalyStdDevMultiplier = 2.0;

    readonly ConcurrentDictionary<string, SensorState> _sensorStates = new();
    long _totalReadings;
    long _alertsRaised;

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            if (record.Value is SensorReading reading) {
                ProcessReading(reading, context.Logger);
            }
        }

        return ValueTask.CompletedTask;
    }

    void ProcessReading(SensorReading reading, ILogger logger) {
        _totalReadings++;

        var state = _sensorStates.GetOrAdd(reading.SensorId, _ => new SensorState());

        // Add reading to rolling window
        state.AddReading(reading.Temperature);

        // Check threshold alerts
        CheckThresholdAlerts(reading, logger);

        // Check for anomalies (need at least 5 readings for meaningful stats)
        if (state.ReadingCount >= 5) {
            CheckAnomalyAlert(reading, state, logger);
        }

        logger.LogDebug(
            "Sensor {SensorId}: {Temp:F1}°C (avg: {Avg:F1}°C, readings: {Count})",
            reading.SensorId,
            reading.Temperature,
            state.Average,
            state.ReadingCount
        );
    }

    void CheckThresholdAlerts(SensorReading reading, ILogger logger) {
        if (reading.Temperature > HighTempThreshold) {
            _alertsRaised++;
            logger.LogWarning(
                "🔥 HIGH TEMPERATURE ALERT | Sensor: {SensorId} | Location: {Location} | Temp: {Temp:F1}°C (threshold: {Threshold}°C)",
                reading.SensorId,
                reading.Location,
                reading.Temperature,
                HighTempThreshold
            );
        } else if (reading.Temperature < LowTempThreshold) {
            _alertsRaised++;
            logger.LogWarning(
                "❄️ LOW TEMPERATURE ALERT | Sensor: {SensorId} | Location: {Location} | Temp: {Temp:F1}°C (threshold: {Threshold}°C)",
                reading.SensorId,
                reading.Location,
                reading.Temperature,
                LowTempThreshold
            );
        }
    }

    void CheckAnomalyAlert(SensorReading reading, SensorState state, ILogger logger) {
        var deviation = Math.Abs(reading.Temperature - state.Average);
        var threshold = state.StandardDeviation * AnomalyStdDevMultiplier;

        if (deviation > threshold && state.StandardDeviation > 0.1) {
            _alertsRaised++;
            logger.LogWarning(
                "⚠️ ANOMALY DETECTED | Sensor: {SensorId} | Temp: {Temp:F1}°C | Expected: {Avg:F1}°C ± {StdDev:F1}°C | Deviation: {Dev:F1}σ",
                reading.SensorId,
                reading.Temperature,
                state.Average,
                state.StandardDeviation,
                deviation / state.StandardDeviation
            );
        }
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} | Readings: {Readings} | Sensors: {Sensors} | Alerts: {Alerts}",
            context.CheckpointId,
            _totalReadings,
            _sensorStates.Count,
            _alertsRaised
        );

        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        context.Logger.LogInformation(
            "Completed | Total readings: {Readings}, Alerts raised: {Alerts}",
            _totalReadings,
            _alertsRaised
        );
        return ValueTask.CompletedTask;
    }

    sealed class SensorState {
        readonly Queue<double> _readings = new();
        double _sum;
        double _sumSquares;

        public int ReadingCount => _readings.Count;
        public double Average => ReadingCount > 0 ? _sum / ReadingCount : 0;

        public double StandardDeviation {
            get {
                if (ReadingCount < 2) return 0;
                var variance = (_sumSquares / ReadingCount) - (Average * Average);
                return variance > 0 ? Math.Sqrt(variance) : 0;
            }
        }

        public void AddReading(double temperature) {
            _readings.Enqueue(temperature);
            _sum += temperature;
            _sumSquares += temperature * temperature;

            // Maintain rolling window
            while (_readings.Count > WindowSize) {
                var removed = _readings.Dequeue();
                _sum -= removed;
                _sumSquares -= removed * removed;
            }
        }
    }
}
