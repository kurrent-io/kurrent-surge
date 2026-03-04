using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class DataSetTests {
    [Test]
    public async Task default_singleton_is_not_null() {
        await Assert.That(HomeAutomationDataSet.Default).IsNotNull();
    }

    [Test]
    public async Task home_has_rooms_and_devices() {
        var home = HomeAutomationDataSet.Default.Home();
        await Assert.That(home.Rooms.Count).IsGreaterThan(0);
        await Assert.That(home.Devices.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task homes_produces_multiple() {
        var homes = HomeAutomationDataSet.Default.Homes(3);
        await Assert.That(homes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task events_produces_requested_count() {
        var events = HomeAutomationDataSet.Default.Events(20);
        await Assert.That(events.Count).IsEqualTo(20);
    }

    [Test]
    public async Task events_reference_valid_device_types() {
        var home   = HomeAutomationDataSet.Default.Home();
        var events = HomeAutomationDataSet.Default.Events(home, 10);

        foreach (var evt in events)
            await Assert.That(evt.Device.DeviceType).IsNotEqualTo(DeviceType.Unspecified);
    }

    [Test]
    public async Task devices_match_subtype_to_device_type_enum() {
        var home = HomeAutomationDataSet.Default.Home();

        foreach (var device in home.Devices) {
            var matches = device switch {
                Thermostat     => device.DeviceType == DeviceType.Thermostat,
                SmartLight     => device.DeviceType == DeviceType.SmartLight,
                DoorLock       => device.DeviceType == DeviceType.DoorLock,
                MotionSensor   => device.DeviceType == DeviceType.MotionSensor,
                SmokeDetector  => device.DeviceType == DeviceType.SmokeDetector,
                HumiditySensor => device.DeviceType == DeviceType.HumiditySensor,
                WindowSensor   => device.DeviceType == DeviceType.WindowSensor,
                WaterSensor    => device.DeviceType == DeviceType.WaterSensor,
                _              => false
            };
            await Assert.That(matches).IsTrue();
        }
    }

    [Test]
    public async Task faker_extension_returns_cached_instance() {
        var faker = new Faker();
        var ds1   = faker.HomeAutomation();
        var ds2   = faker.HomeAutomation();
        await Assert.That(ds1).IsSameReferenceAs(ds2);
    }

    [Test]
    public async Task infinite_events_stream_produces_events() {
        var events = HomeAutomationDataSet.Default.Events().Take(50).ToList();
        await Assert.That(events.Count).IsEqualTo(50);
    }
}
