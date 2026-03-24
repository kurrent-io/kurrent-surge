using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Tests;

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
        await Assert.That(home.Devices.Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task homes_produces_multiple() {
        var homes = HomeAutomationDataSet.Default.Homes().Take(3).ToList();
        await Assert.That(homes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task events_produces_requested_count() {
        var events = HomeAutomationDataSet.Default.Events().Take(20).ToList();
        await Assert.That(events.Count).IsEqualTo(20);
    }

    [Test]
    public async Task devices_match_subtype_to_pattern() {
        var home = HomeAutomationDataSet.Default.Home();

        foreach (var device in home.Devices) {
            var matched = device switch {
                Thermostat     => true,
                SmartLight     => true,
                DoorLock       => true,
                MotionSensor   => true,
                SmokeDetector  => true,
                HumiditySensor => true,
                WindowSensor   => true,
                WaterSensor    => true,
                _              => false
            };

            await Assert.That(matched).IsTrue();
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

    [Test]
    public async Task events_for_home_produces_events() {
        var home   = HomeAutomationDataSet.Default.Home();
        var events = HomeAutomationDataSet.Default.Events(home).Take(10).ToList();
        await Assert.That(events.Count).IsEqualTo(10);
    }
}
