// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Commands;

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

public record RecordDoorStateChanged : ISmartHomeCommand {
    public required string     DeviceId   { get; init; }
    public required bool       IsOpen     { get; init; }
    public required LockStatus LockStatus { get; init; }
    public required string     UserId     { get; init; }
}

public record RecordLightStateChanged : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required bool   IsOn       { get; init; }
    public required int    Brightness { get; init; }
    public required string Color      { get; init; }
}

public record RecordWindowStateChanged : ISmartHomeCommand {
    public required string DeviceId       { get; init; }
    public required bool   IsOpen         { get; init; }
    public required int    OpenPercentage { get; init; }
}

public record RecordSmokeState : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required bool   HasSmoke   { get; init; }
    public required int    SmokeLevel { get; init; }
}

public record RecordWaterLeakState : ISmartHomeCommand {
    public required string DeviceId      { get; init; }
    public required bool   HasLeak       { get; init; }
    public required int    MoistureLevel { get; init; }
}
