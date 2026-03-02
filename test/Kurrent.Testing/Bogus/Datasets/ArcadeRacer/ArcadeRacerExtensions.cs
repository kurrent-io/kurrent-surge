// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

public static class ArcadeRacerExtensions {
    extension(Faker faker) {
        public ArcadeRacerDataSet ArcadeRacer() =>
            ContextHelper.GetOrSet(faker, f => new ArcadeRacerDataSet(f));
    }
}
