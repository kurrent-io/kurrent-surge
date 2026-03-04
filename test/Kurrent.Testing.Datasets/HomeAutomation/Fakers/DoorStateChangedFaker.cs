using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DoorStateChangedFaker : Faker<DoorStateChanged> {
    public DoorStateChangedFaker(DoorLock device, long timestamp) {
        var isOpen = false;
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(e => e.IsOpen, f => {
                isOpen = f.Random.Bool(0.3f);
                return isOpen;
            })
            .RuleFor(e => e.LockStatus, f => isOpen ? LockStatus.Unlocked : f.PickRandom<LockStatus>())
            .RuleFor(e => e.UserId, f => f.Internet.UserName())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
