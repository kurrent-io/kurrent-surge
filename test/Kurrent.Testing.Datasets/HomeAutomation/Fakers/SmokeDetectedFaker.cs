using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class SmokeEventFaker(Device device, Guid roomId, long timestamp) {
    readonly Faker _faker = new();

    public IHomeAutomationEvent Generate() {
        var smoke = (SmokeDetector)device;
        var info = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = device.IsBatteryPowered ? device.BatteryLevel : null
        };

        if (_faker.Random.Bool(0.005f)) {
            return new SmokeAlarmTriggered {
                Device     = info,
                SmokeLevel = _faker.Random.Number(smoke.AlarmThreshold / 2, smoke.AlarmThreshold),
                Timestamp  = timestamp
            };
        }

        return new SmokeAlarmCleared {
            Device    = info,
            Timestamp = timestamp
        };
    }
}
