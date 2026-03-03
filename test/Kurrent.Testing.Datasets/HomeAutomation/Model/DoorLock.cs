namespace Testing.Datasets.HomeAutomation.Model;

public class DoorLock(Room room, string deviceId)
    : Device(room, DeviceType.DoorLock, false, deviceId ) {
    public bool       IsOpen     { get; set; }
    public LockStatus LockStatus { get; set; } = LockStatus.Locked;

    public void ToggleDoor() {
        IsOpen = !IsOpen;
        if (IsOpen) LockStatus = LockStatus.Unlocked;
    }

    public void SetLockStatus(LockStatus status) {
        LockStatus = status;
        if (status == LockStatus.Locked) IsOpen = false;
    }
}