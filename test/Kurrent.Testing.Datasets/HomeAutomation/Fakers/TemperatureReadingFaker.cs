// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedType.Global

using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class TemperatureReadingFaker : Faker<TemperatureReading> {
    public TemperatureReadingFaker(Thermostat device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.Temperature, f => {
                    var change = f.Random.Double(-0.5, 0.5);
                    device.UpdateTemperature(device.CurrentTemperature + change);

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return Math.Round(device.CurrentTemperature, 1);
                }
            )
            .RuleFor(e => e.Unit, TemperatureUnit.Celsius)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
