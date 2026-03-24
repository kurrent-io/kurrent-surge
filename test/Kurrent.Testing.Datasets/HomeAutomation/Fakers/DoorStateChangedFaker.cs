using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class DoorEventFaker(Device device, Guid roomId, long timestamp) {
    readonly Faker _faker = new();

    public IHomeAutomationEvent Generate() {
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        return _faker.PickRandom<int>(0, 1, 2, 3) switch {
            0 => new DoorOpened   { Device = info, UserId = _faker.Internet.UserName(), Timestamp = timestamp },
            1 => new DoorClosed   { Device = info, UserId = _faker.Internet.UserName(), Timestamp = timestamp },
            2 => new DoorLocked   { Device = info, UserId = _faker.Internet.UserName(), Timestamp = timestamp },
            _ => new DoorUnlocked { Device = info, UserId = _faker.Internet.UserName(), Timestamp = timestamp }
        };
    }
}
