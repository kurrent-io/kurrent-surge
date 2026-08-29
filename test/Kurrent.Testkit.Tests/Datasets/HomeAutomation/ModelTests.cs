using Kurrent.Testkit.Datasets.HomeAutomation;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

// ReSharper disable InconsistentNaming

namespace Kurrent.Testkit.Tests.Datasets.HomeAutomation;

[Category("HomeAutomation")]
public class ModelTests {
    static Room MakeRoom() => new() { RoomId = TestId.New(), Name = "Test Room", Type = RoomType.LivingRoom, Devices = [] };

    [Test]
    public async ValueTask device_creates_device_info_with_battery() {
        var room = MakeRoom();
        var dev = new Thermostat {
            Id                   = "t1",
            FriendlyName         = "Thermo",
            IsBatteryPowered     = true,
            BatteryLevel         = 85,
            MinOperatingTemp     = 5,
            MaxOperatingTemp     = 45,
            MeasurementPrecision = 1,
            PreferredUnit        = TemperatureUnit.Celsius
        };

        var info = new DeviceInfo {
            Id           = dev.Id,
            RoomId       = room.RoomId,
            BatteryLevel = dev.IsBatteryPowered ? dev.BatteryLevel : null
        };

        await Assert.That(info.Id).IsEqualTo("t1");
        await Assert.That(info.RoomId).IsEqualTo(room.RoomId);
        await Assert.That(info.BatteryLevel).IsEqualTo(85);
    }

    [Test]
    public async ValueTask device_creates_device_info_without_battery_for_mains_powered() {
        var room = MakeRoom();
        var dev = new SmartLight {
            Id                     = "l1",
            FriendlyName           = "Light",
            IsBatteryPowered       = false,
            BatteryLevel           = null,
            MaxBrightness          = 100,
            SupportsColor          = true,
            WattsPerBrightnessUnit = 0.6
        };

        var info = new DeviceInfo {
            Id           = dev.Id,
            RoomId       = room.RoomId,
            BatteryLevel = dev.IsBatteryPowered ? dev.BatteryLevel : null
        };

        await Assert.That(info.BatteryLevel).IsNull();
    }

    #region ->> Device Faker Tests <<-

    [Test]
    public async ValueTask thermostat_faker_generates_valid_device() {
        var device = ThermostatFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.MinOperatingTemp).IsLessThan(device.MaxOperatingTemp);
        await Assert.That(device.MeasurementPrecision).IsGreaterThan(0);
        await Assert.That(device.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async ValueTask motion_sensor_faker_generates_valid_device() {
        var device = MotionSensorFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.ZoneCount).IsBetween(2, 6);
        await Assert.That(device.DetectionRangeMeters).IsGreaterThanOrEqualTo(3.0);
        await Assert.That(device.IsBatteryPowered).IsFalse();
    }

    [Test]
    public async ValueTask smart_light_faker_generates_valid_device() {
        var device = SmartLightFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.MaxBrightness).IsGreaterThan(0);
        await Assert.That(device.WattsPerBrightnessUnit).IsGreaterThan(0);
        await Assert.That(device.IsBatteryPowered).IsFalse();
    }

    [Test]
    public async ValueTask door_lock_faker_generates_valid_device() {
        var device = DoorLockFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.IsBatteryPowered).IsFalse();
    }

    [Test]
    public async ValueTask smoke_detector_faker_generates_valid_device() {
        var device = SmokeDetectorFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.SensorType).IsNotNull();
        await Assert.That(device.AlarmThreshold).IsBetween(40, 80);
        await Assert.That(device.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async ValueTask humidity_sensor_faker_generates_valid_device() {
        var device = HumiditySensorFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.MeasurementTolerance).IsGreaterThanOrEqualTo(1.0);
        await Assert.That(device.MeasurementPrecision).IsGreaterThan(0);
        await Assert.That(device.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async ValueTask window_sensor_faker_generates_valid_device() {
        var device = WindowSensorFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async ValueTask water_sensor_faker_generates_valid_device() {
        var device = WaterSensorFaker.Instance.Generate();
        await Assert.That(device.Id).IsNotNull();
        await Assert.That(device.MoistureThreshold).IsBetween(30, 70);
        await Assert.That(device.IsBatteryPowered).IsTrue();
    }

    #endregion
}
