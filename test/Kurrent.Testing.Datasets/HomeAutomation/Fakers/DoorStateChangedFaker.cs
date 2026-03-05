using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DoorStateChangedFaker : Faker<DoorStateChanged> {
    public DoorStateChangedFaker(DoorLock device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.IsOpen, f => {
                    if (f.Random.Bool(0.05f)) device.ToggleDoor();
                    if (!device.IsOpen && f.Random.Bool(0.2f))
                        device.SetLockStatus(f.Random.Bool(0.8f) ? LockStatus.Locked : LockStatus.Unlocked);

                    return device.IsOpen;
                }
            )
            .RuleFor(e => e.LockStatus, f => device.LockStatus)
            .RuleFor(e => e.UserId, f => f.Internet.UserName())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
