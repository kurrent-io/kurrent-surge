// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedType.Global
// ReSharper disable RedundantCallerArgumentExpressionDefaultValue
// ReSharper disable UnusedMember.Global

using Bogus;

namespace Kurrent.Testing.Sample.HomeAutomation;

/// <summary>
/// Bogus extension to enable faker.HomeAutomation() syntax
/// </summary>
public static class FakerExtensions {
    public static HomeAutomationDataSet HomeAutomation(this Faker faker, string locale = "en") {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return faker.Locale != locale ? new HomeAutomationDataSet(locale) : new HomeAutomationDataSet(faker);
    }
}
