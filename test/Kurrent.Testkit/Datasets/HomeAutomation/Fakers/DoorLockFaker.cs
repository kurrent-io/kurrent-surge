using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation;

public class DoorLockFaker : Faker<DoorLock> {
    public static readonly DoorLockFaker Instance = new();

    public DoorLockFaker() {
        CustomInstantiator(f => new DoorLock {
            Id               = $"urn:iot:doorlock:{TestId.New()}",
            FriendlyName     = $"Door Lock {f.Random.Number(1, 99)}",
            IsBatteryPowered = false,
            BatteryLevel     = null,
            HasKeypad        = f.Random.Bool(0.5f),
            HasAutoLock      = f.Random.Bool(0.4f)
        });
    }
}
