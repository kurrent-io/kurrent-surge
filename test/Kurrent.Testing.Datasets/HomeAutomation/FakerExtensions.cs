// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Testing.Datasets.HomeAutomation;

public static class FakerExtensions {
    extension(Faker faker) {
        public HomeAutomationDataSet HomeAutomation() =>
            ContextHelper.GetOrSet(faker, f => new HomeAutomationDataSet(f));
    }
}
