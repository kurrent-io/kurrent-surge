// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Contracts.Commands;

public interface ISmartHomeCommand {
    string DeviceId { get; }
}

public record RecordTemperature : ISmartHomeCommand {
    public required string          DeviceId    { get; init; }
    public required double          Temperature { get; init; }
    public required TemperatureUnit Unit        { get; init; }
}

public record RecordHumidity : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required double Humidity { get; init; }
}

public record RecordMotion : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required double Confidence { get; init; }
    public required int    ZoneId     { get; init; }
}

public record RecordDoorOpen : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorClose : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorLock : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorUnlock : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordWindowOpen : ISmartHomeCommand {
    public required string DeviceId       { get; init; }
    public required int    OpenPercentage { get; init; }
}

public record RecordWindowClose : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordLightOn : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required int    Brightness { get; init; }
    public required string Color      { get; init; }
}

public record RecordLightOff : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordSmokeDetected : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required int    SmokeLevel { get; init; }
}

public record RecordSmokeCleared : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordWaterLeakDetected : ISmartHomeCommand {
    public required string DeviceId      { get; init; }
    public required int    MoistureLevel { get; init; }
}

public record RecordWaterLeakResolved : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}
