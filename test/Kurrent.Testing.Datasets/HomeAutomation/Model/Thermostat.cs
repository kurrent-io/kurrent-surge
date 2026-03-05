namespace Testing.Datasets.HomeAutomation.Model;

public class Thermostat(Room room, string deviceId)
    : Device(room, DeviceType.Thermostat, true, deviceId) {
    public int    BatteryLevel       { get; set; } = 85;
    public double CurrentTemperature { get; set; } = 22.0;

    public void UpdateTemperature(double newTemperature) =>
        CurrentTemperature = Math.Max(5.0, Math.Min(45.0, newTemperature));

    public void DepleteBattery(int amount = 1) =>
        BatteryLevel = Math.Max(5, BatteryLevel - amount);
}