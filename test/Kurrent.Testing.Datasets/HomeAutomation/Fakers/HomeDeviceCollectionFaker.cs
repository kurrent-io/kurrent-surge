using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HomeDeviceCollectionFaker : Faker<List<Device>> {
    public HomeDeviceCollectionFaker(SmartHome home, int deviceCount) {
        CustomInstantiator(f => {
                var devices             = new List<Device>();
                var usedRoomDevicePairs = new HashSet<string>();
                var attempts            = 0;
                var maxAttempts         = deviceCount * 3; // Prevent infinite loops

                while (devices.Count < deviceCount && attempts < maxAttempts) {
                    attempts++;
                    var room        = f.PickRandom(home.Rooms);
                    var deviceFaker = new DeviceFaker(room, homeId: home.Id);
                    var device      = deviceFaker.Generate();

                    var roomDeviceKey = $"{room.RoomId}_{device.DeviceType}";

                    // Allow some duplicates once we have enough variety
                    var allowDuplicate = usedRoomDevicePairs.Count >= deviceCount / 2;

                    if (!usedRoomDevicePairs.Contains(roomDeviceKey) || allowDuplicate) {
                        devices.Add(device);
                        usedRoomDevicePairs.Add(roomDeviceKey);
                    }
                }

                return devices;
            }
        );
    }
}