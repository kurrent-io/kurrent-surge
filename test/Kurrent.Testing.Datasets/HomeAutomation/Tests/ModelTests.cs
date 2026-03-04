using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class ModelTests {
    static Room MakeRoom() => new() { RoomId = Guid.NewGuid(), Name = "Test Room", Type = RoomType.LivingRoom };

    [Test]
    public async Task room_is_record_with_value_equality() {
        var id   = Guid.NewGuid();
        var a    = new Room { RoomId = id, Name = "Room", Type = RoomType.Kitchen };
        var b    = new Room { RoomId = id, Name = "Room", Type = RoomType.Kitchen };
        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task device_subtypes_support_pattern_matching() {
        var room   = MakeRoom();
        Device dev = new Thermostat {
            Id = "t1", FriendlyName = "Thermo", Room = room,
            DeviceType = DeviceType.Thermostat, IsBatteryPowered = true
        };

        var matched = dev switch {
            Thermostat     => "thermostat",
            SmartLight     => "light",
            DoorLock       => "door",
            MotionSensor   => "motion",
            SmokeDetector  => "smoke",
            HumiditySensor => "humidity",
            WindowSensor   => "window",
            WaterSensor    => "water",
            _              => "unknown"
        };

        await Assert.That(matched).IsEqualTo("thermostat");
    }

    [Test]
    public async Task device_creates_device_info_with_battery() {
        var room = MakeRoom();
        var dev  = new Thermostat {
            Id = "t1", FriendlyName = "Thermo", Room = room,
            DeviceType = DeviceType.Thermostat, IsBatteryPowered = true
        };
        var info = dev.CreateDeviceInfo(85);

        await Assert.That(info.Id).IsEqualTo("t1");
        await Assert.That(info.BatteryLevel).IsEqualTo(85);
        await Assert.That(info.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async Task device_creates_device_info_without_battery_for_mains_powered() {
        var room = MakeRoom();
        var dev  = new SmartLight {
            Id = "l1", FriendlyName = "Light", Room = room,
            DeviceType = DeviceType.SmartLight, IsBatteryPowered = false
        };
        var info = dev.CreateDeviceInfo(50);

        await Assert.That(info.BatteryLevel).IsNull();
    }

    [Test]
    public async Task smart_home_is_record_with_with_expression() {
        var room  = MakeRoom();
        var rooms = new List<Room> { room };
        var devs  = new List<Device>();
        var a = new SmartHome { Id = "h1", Name = "Home", Rooms = rooms, Devices = devs, CreatedAt = DateTime.UnixEpoch };
        var b = a with { Name = "Other Home" };

        await Assert.That(b.Name).IsEqualTo("Other Home");
        await Assert.That(b.Id).IsEqualTo(a.Id);
        await Assert.That(b.Rooms).IsSameReferenceAs(a.Rooms);
    }
}
