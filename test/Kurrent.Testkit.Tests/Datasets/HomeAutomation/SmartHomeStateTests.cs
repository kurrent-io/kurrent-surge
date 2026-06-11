using Kurrent.Testkit.Datasets;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Commands;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Tests.Datasets.HomeAutomation;

[Category("HomeAutomation")]
public class SmartHomeStateTests {
    static SmartHomeState CreateState(params Device[] devices) {
        var room = new Room {
            RoomId  = TestId.New(),
            Name    = "Test",
            Type    = RoomType.LivingRoom,
            Devices = [..devices]
        };

        var home = new SmartHome {
            Id        = "h1",
            Name      = "Test Home",
            Rooms     = [room],
            CreatedAt = DateTime.UtcNow
        };

        var state = new SmartHomeState();
        state.Initialize(home, 1000L);

        return state;
    }

    static Thermostat MakeThermo() =>
        new() {
            Id                   = "t1",
            FriendlyName         = "Thermo",
            IsBatteryPowered     = true,
            BatteryLevel         = 100,
            MinOperatingTemp     = 5,
            MaxOperatingTemp     = 45,
            MeasurementPrecision = 1,
            PreferredUnit        = TemperatureUnit.Celsius
        };

    static SmartLight MakeLight() =>
        new() {
            Id                     = "l1",
            FriendlyName           = "Light",
            IsBatteryPowered       = false,
            BatteryLevel           = null,
            MaxBrightness          = 100,
            SupportsColor          = true,
            WattsPerBrightnessUnit = 0.6
        };

    #region ->> Execute produces correct events <<-

    [Test]
    public async Task execute_record_temperature_produces_temperature_reading() {
        var state = CreateState(MakeThermo());

        var cmd = new RecordTemperature {
            DeviceId    = "t1",
            Temperature = 22.5,
            Unit        = TemperatureUnit.Celsius
        };

        var evt = state.Execute(cmd, 2000L);

        await Assert.That(evt).IsTypeOf<TemperatureReading>();
        var tr = (TemperatureReading)evt;
        await Assert.That(tr.Temperature).IsEqualTo(22.5);
        await Assert.That(tr.Device.Id).IsEqualTo("t1");
    }

    [Test]
    public async Task execute_record_light_on_produces_light_turned_on() {
        var state = CreateState(MakeLight());

        var cmd = new RecordLightStateChanged {
            DeviceId   = "l1",
            IsOn       = true,
            Brightness = 80,
            Color      = "#FFD700"
        };

        var evt = state.Execute(cmd, 2000L);

        await Assert.That(evt).IsTypeOf<LightTurnedOn>();
        var lsc = (LightTurnedOn)evt;
        await Assert.That(lsc.Brightness).IsEqualTo(80);
    }

    #endregion

    #region ->> Apply updates state <<-

    [Test]
    public async Task apply_temperature_reading_updates_current_temperature() {
        var state = CreateState(MakeThermo());

        var cmd = new RecordTemperature {
            DeviceId    = "t1",
            Temperature = 25.0,
            Unit        = TemperatureUnit.Celsius
        };

        var evt = state.Execute(cmd, 2000L);
        state.Apply(evt);

        await Assert.That(state.Devices["t1"].CurrentTemperature).IsEqualTo(25.0);
    }

    [Test]
    public async Task apply_light_on_updates_light_state() {
        var state = CreateState(MakeLight());

        var cmd = new RecordLightStateChanged {
            DeviceId   = "l1",
            IsOn       = true,
            Brightness = 70,
            Color      = "#FFF"
        };

        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["l1"].LightIsOn).IsTrue();
        await Assert.That(state.Devices["l1"].Brightness).IsEqualTo(70);
    }

    [Test]
    public async Task apply_updates_current_time() {
        var state = CreateState(MakeThermo());

        var cmd = new RecordTemperature {
            DeviceId    = "t1",
            Temperature = 20.0,
            Unit        = TemperatureUnit.Celsius
        };

        state.Apply(state.Execute(cmd, 5000L));

        await Assert.That(state.CurrentTime).IsEqualTo(5000L);
    }

    #endregion

    #region ->> Battery drain <<-

    [Test]
    public async Task battery_drains_on_battery_powered_device() {
        var state   = CreateState(MakeThermo());
        var initial = state.Devices["t1"].BatteryLevel;

        var cmd = new RecordTemperature {
            DeviceId    = "t1",
            Temperature = 20.0,
            Unit        = TemperatureUnit.Celsius
        };

        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["t1"].BatteryLevel).IsLessThan(initial);
    }

    [Test]
    public async Task battery_stable_on_mains_powered_device() {
        var state   = CreateState(MakeLight());
        var initial = state.Devices["l1"].BatteryLevel;

        var cmd = new RecordLightStateChanged {
            DeviceId   = "l1",
            IsOn       = true,
            Brightness = 50,
            Color      = "#FFF"
        };

        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["l1"].BatteryLevel).IsEqualTo(initial);
    }

    #endregion

    #region ->> Error handling <<-

    [Test]
    public async Task execute_rejects_unknown_device_id() {
        var state = CreateState(MakeThermo());

        var cmd = new RecordTemperature {
            DeviceId    = "unknown",
            Temperature = 20.0,
            Unit        = TemperatureUnit.Celsius
        };

        await Assert.That(() => state.Execute(cmd, 2000L)).Throws<InvalidOperationException>();
    }

    #endregion
}
