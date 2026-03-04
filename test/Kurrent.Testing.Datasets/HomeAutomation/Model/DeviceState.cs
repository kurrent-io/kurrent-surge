namespace Testing.Datasets.HomeAutomation.Model;

public class DeviceState {
    public required string     DeviceId         { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }

    // Battery
    public int BatteryLevel { get; set; } = 100;

    // Thermostat
    public double CurrentTemperature { get; set; } = 22.0;

    // Light
    public bool   LightIsOn     { get; set; }
    public int    Brightness    { get; set; }
    public string LightColor    { get; set; } = "#FFFFFF";

    // Door
    public bool       DoorIsOpen   { get; set; }
    public LockStatus DoorLock     { get; set; } = LockStatus.Locked;

    // Window
    public bool IsWindowOpen    { get; set; }
    public int  OpenPercentage  { get; set; }

    // Humidity
    public double CurrentHumidity { get; set; } = 45.0;

    // Smoke
    public bool SmokeAlarmActive { get; set; }

    // Water
    public bool WaterLeakDetected { get; set; }

    public void DrainBattery(int amount = 1) {
        if (IsBatteryPowered)
            BatteryLevel = Math.Max(0, BatteryLevel - amount);
    }
}
