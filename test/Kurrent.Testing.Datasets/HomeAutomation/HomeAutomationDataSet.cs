// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable IteratorNeverReturns

using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public interface IHomeAutomationDataSet {
    SmartHome Home(int? rooms = null, int? devicesPerRoom = null);
    IEnumerable<SmartHome> Homes(int? rooms = null, int? devicesPerRoom = null);
    Room Room(RoomType? roomType = null, int? devicesPerRoom = null);
    IEnumerable<Room> Rooms(int? devicesPerRoom = null);
    Device Device(DeviceType? deviceType = null);
    IEnumerable<Device> Devices(DeviceType? deviceType = null);
    IEnumerable<IHomeAutomationEvent> Events(params Device[] devices);
    IEnumerable<IHomeAutomationEvent> Events(Room room);
    IEnumerable<IHomeAutomationEvent> Events(SmartHome home);
}

public class HomeAutomationDataSet(Faker? faker = null) : CustomDataSet<HomeAutomationDataSet>(faker ?? new Faker()), IHomeAutomationDataSet {
    public static readonly HomeAutomationDataSet Default = new();

    protected override void Initialize() { }

    public SmartHome Home(int? rooms = null, int? devicesPerRoom = null) =>
        new HomeFaker(rooms, devicesPerRoom).Generate();

    public IEnumerable<SmartHome> Homes(int? rooms = null, int? devicesPerRoom = null) =>
        new HomeFaker(rooms, devicesPerRoom).GenerateForever();

    public Room Room(RoomType? roomType = null, int? devicesPerRoom = null) {
        if (roomType is null)
            return new RoomFaker(devicesPerRoom).Generate();

        var room = new RoomFaker(devicesPerRoom).Generate();
        return room with { Type = roomType.Value };
    }

    public IEnumerable<Room> Rooms(int? devicesPerRoom = null) =>
        new RoomFaker(devicesPerRoom).GenerateForever();

    public Device Device(DeviceType? deviceType = null) =>
        new DeviceFaker(deviceType: deviceType).Generate();

    public IEnumerable<Device> Devices(DeviceType? deviceType = null) =>
        new DeviceFaker(deviceType: deviceType).GenerateForever();

    public IEnumerable<IHomeAutomationEvent> Events(params Device[] devices) {
        if (devices.Length == 0)
            devices = Devices().Take(Faker.Random.Number(3, 7)).ToArray();

        var timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        while (true) {
            timestamp += Faker.Random.Number(1, 30) * 60000L;
            var device = Faker.PickRandom(devices);
            yield return GenerateEventForDevice(device, Guid.Empty, timestamp);
        }
    }

    public IEnumerable<IHomeAutomationEvent> Events(Room room) {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        while (true) {
            timestamp += Faker.Random.Number(1, 30) * 60000L;
            var device = Faker.PickRandom(room.Devices);
            yield return GenerateEventForDevice(device, room.RoomId, timestamp);
        }
    }

    public IEnumerable<IHomeAutomationEvent> Events(SmartHome home) {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        while (true) {
            timestamp += Faker.Random.Number(1, 30) * 60000L;
            var room   = Faker.PickRandom(home.Rooms);
            var device = Faker.PickRandom(room.Devices);
            yield return GenerateEventForDevice(device, room.RoomId, timestamp);
        }
    }

    static IHomeAutomationEvent GenerateEventForDevice(Device device, Guid roomId, long timestamp) {
        return device switch {
            Thermostat     => new TemperatureReadingFaker(device, roomId, timestamp).Generate(),
            MotionSensor   => new MotionDetectedFaker(device, roomId, timestamp).Generate(),
            SmartLight     => new LightEventFaker(device, roomId, timestamp).Generate(),
            DoorLock       => new DoorEventFaker(device, roomId, timestamp).Generate(),
            SmokeDetector  => new SmokeEventFaker(device, roomId, timestamp).Generate(),
            HumiditySensor => new HumidityReadingFaker(device, roomId, timestamp).Generate(),
            WindowSensor   => new WindowEventFaker(device, roomId, timestamp).Generate(),
            WaterSensor    => new WaterLeakEventFaker(device, roomId, timestamp).Generate(),
            _              => new TemperatureReadingFaker(device, roomId, timestamp).Generate()
        };
    }
}
