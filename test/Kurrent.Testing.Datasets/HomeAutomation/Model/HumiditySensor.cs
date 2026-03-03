namespace Testing.Datasets.HomeAutomation.Model;

public class HumiditySensor(Room room, string deviceId)
    : Device(room, DeviceType.HumiditySensor, true, deviceId) {
    public int    BatteryLevel  { get; set; } = 85;
    public double HumidityLevel { get; set; } = 45.0; // Starting at 45% humidity

    public void UpdateHumidity(double newHumidity) =>
        HumidityLevel = Math.Max(0.0, Math.Min(100.0, newHumidity));

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}