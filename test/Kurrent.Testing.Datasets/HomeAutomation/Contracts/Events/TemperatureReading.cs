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

#region Door events

#endregion

#region Light events

#endregion

#region Smoke events

#endregion

#region Water events

#endregion

#region Window events

#endregion
