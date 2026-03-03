namespace Testing.Datasets.HomeAutomation.Model;

public class WindowSensor(Room room, string deviceId)
    : Device(room, DeviceType.WindowSensor, true, deviceId) {
    public int  BatteryLevel { get; set; } = 90;
    public bool IsOpen       { get; set; } = false;
    public long LastOpenTime { get; set; } = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

    public void ToggleWindow() {
        IsOpen = !IsOpen;
        if (IsOpen) RecordOpening(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void RecordOpening(long timestamp) => LastOpenTime = timestamp;

    public void DepleteBattery(int amount = 1) => BatteryLevel = Math.Max(5, BatteryLevel - amount);
}