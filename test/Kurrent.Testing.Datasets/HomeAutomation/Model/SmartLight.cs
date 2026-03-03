namespace Testing.Datasets.HomeAutomation.Model;

public class SmartLight(Room room, string deviceId) : Device(room, DeviceType.SmartLight, false, deviceId ) {
    public bool IsOn       { get; set; }
    public int  Brightness { get; set; }

    public void ToggleLight() {
        IsOn = !IsOn;
        if (!IsOn) Brightness = 0;
    }

    public void SetBrightness(int brightness) {
        if (IsOn) Brightness = Math.Max(0, Math.Min(100, brightness));
    }
}