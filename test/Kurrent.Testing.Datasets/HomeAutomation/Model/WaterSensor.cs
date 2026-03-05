namespace Testing.Datasets.HomeAutomation.Model;

public class WaterSensor(Room room, string deviceId)
    : Device(room, DeviceType.WaterSensor, true, deviceId) {
    public int  BatteryLevel  { get; set; } = 95;
    public bool WaterDetected { get; set; } = false;
    public int  WaterLevel    { get; set; } = 0; // 0-100 scale

    public void DetectWater(bool hasWater = true) {
        WaterDetected = hasWater;
        if (!hasWater) WaterLevel = 0;
    }

    public void UpdateWaterLevel(int level) {
        WaterLevel    = Math.Max(0, Math.Min(100, level));
        WaterDetected = WaterLevel > 0;
    }

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}