namespace Testing.Datasets.HomeAutomation.Model;

public class MotionSensor(Room room, string deviceId)
    : Device(room, DeviceType.MotionSensor, false, deviceId) {
    public long LastMotionTime { get; set; } = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();

    public void RecordMotion(long timestamp) => LastMotionTime = timestamp;
}