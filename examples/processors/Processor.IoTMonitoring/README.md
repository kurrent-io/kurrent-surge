# Processor.IoTMonitoring

Real-time IoT sensor monitoring with threshold alerts and anomaly detection.

## What You'll Learn

- Rolling window calculations for streaming data
- Threshold-based alerting
- Statistical anomaly detection (standard deviation)
- Maintaining per-sensor state

## How It Works

1. **Input**: `SensorReading` events from IoT devices
2. **Processing**:
   - Track rolling window of last 10 readings per sensor
   - Calculate running average and standard deviation
   - Check for threshold violations (>80°C or <10°C)
   - Detect anomalies (readings > 2σ from mean)
3. **Output**: Log alerts when conditions are met

```
SensorReading(sensor1, 25.0°C)  → OK (baseline)
SensorReading(sensor1, 26.0°C)  → OK
SensorReading(sensor1, 85.0°C)  → 🔥 HIGH TEMP ALERT + ⚠️ ANOMALY
SensorReading(sensor1, 5.0°C)   → ❄️ LOW TEMP ALERT + ⚠️ ANOMALY
```

## Prerequisites

- KurrentDB running on `localhost:2113`

## Running the Example

```bash
cd examples/processors/Processor.IoTMonitoring
dotnet run
```

A hosted `SensorReadingSeeder` writes readings for three sensors and spikes or drops every tenth
round, so both the threshold alerts and the anomaly detector fire. Press `Ctrl+C` to stop.

## Key Code

```csharp
void CheckAnomalyAlert(SensorReading reading, SensorState state) {
    var deviation = Math.Abs(reading.Temperature - state.Average);
    var threshold = state.StandardDeviation * AnomalyStdDevMultiplier;

    if (deviation > threshold && state.StandardDeviation > 0.1) {
        logger.LogWarning(
            "⚠️ ANOMALY DETECTED | Sensor: {SensorId} | Temp: {Temp:F1}°C",
            reading.SensorId, reading.Temperature
        );
    }
}
```

## Alert Types

| Alert            | Condition              | Icon |
|------------------|------------------------|------|
| High Temperature | > 80°C                 | 🔥   |
| Low Temperature  | < 10°C                 | ❄️   |
| Anomaly          | > 2σ from rolling mean | ⚠️   |

## Sample Output

```
[INF] Initializing TemperatureMonitorProcessor
[INF] Thresholds: High=80°C, Low=10°C, Anomaly=2σ
[WRN] 🔥 HIGH TEMPERATURE ALERT | Sensor: temp-001 | Location: Server Room A | Temp: 85.2°C (threshold: 80°C)
[WRN] ⚠️ ANOMALY DETECTED | Sensor: temp-001 | Temp: 85.2°C | Expected: 24.5°C ± 2.1°C | Deviation: 28.9σ
[INF] Checkpoint #1 | Readings: 150 | Sensors: 12 | Alerts: 3
```

## Original Tutorials

- [Azure Stream Analytics IoT Tutorial](https://learn.microsoft.com/en-us/azure/stream-analytics/stream-analytics-get-started-with-azure-stream-analytics-to-process-data-from-iot-devices)
- [Apache Flink CEP for IoT](https://nightlies.apache.org/flink/flink-docs-stable/docs/libs/cep/)
