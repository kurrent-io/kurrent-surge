namespace Testing.Datasets.HomeAutomation.Model;

public class SmokeDetector(Room room, string deviceId)
    : Device(room, DeviceType.SmokeDetector, true, deviceId) {
    public int BatteryLevel { get; set; } = 90;

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}