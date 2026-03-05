// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable UnusedParameter.Local
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

using Testing.Datasets.HomeAutomation.Model;


namespace Testing.Datasets.HomeAutomation.Contracts.Events;

public record TemperatureReading : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo      Device      { get; init; }
    public required double          Temperature { get; init; }
    public required TemperatureUnit Unit        { get; init; }
    public required long            Timestamp   { get; init; }
}

public record MotionDetected : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}

public record DoorStateChanged : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device     { get; init; }
    public required bool       IsOpen     { get; init; }
    public required LockStatus LockStatus { get; init; }
    public required string     UserId     { get; init; }
    public required long       Timestamp  { get; init; }
}

public record WindowStateChanged : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo       Device           { get; init; }
    public required bool             IsOpen           { get; init; }
    public required int              OpenPercentage   { get; init; }
    public required WeatherCondition WeatherCondition { get; init; }
    public required long             Timestamp        { get; init; }
}

public record SmokeDetected : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo  Device      { get; init; }
    public required bool        HasSmoke    { get; init; }
    public required int         SmokeLevel  { get; init; }
    public required AlarmStatus AlarmStatus { get; init; }
    public required long        Timestamp   { get; init; }
}

public record WaterLeakDetected : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device        { get; init; }
    public required bool       HasWater      { get; init; }
    public required int        MoistureLevel { get; init; }
    public required bool       AlertSent     { get; init; }
    public required long       Timestamp     { get; init; }
}

public record LightStateChanged : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo Device           { get; init; }
    public required bool       IsOn             { get; init; }
    public required int        Brightness       { get; init; }
    public required string     Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}

public record HumidityReading : IHomeAutomationEvent {
    public Guid EventId { get; init; } = Guid.NewGuid();

    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}

public record DeviceInfo {
    public required string     Id               { get; init; }
    public required string     Name             { get; init; }
    public required Guid       RoomId           { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }
    public          int?       BatteryLevel     { get; init; }
}

public interface IHomeAutomationEvent {
    Guid       EventId   { get; init; }
    DeviceInfo Device    { get; init; }
    long       Timestamp { get; init; }
}
