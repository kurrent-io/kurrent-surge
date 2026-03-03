// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testing.Datasets;

namespace Testing.Datasets.ArcadeRacer;

/// <summary>C# 14 extension providing <see cref="ArcadeRacerDataSet"/> access on <see cref="Faker"/>.</summary>
public static class ArcadeRacerExtensions {
    extension(Faker faker) {
        /// <summary>Gets or creates the <see cref="ArcadeRacerDataSet"/> for this <see cref="Faker"/> instance.</summary>
        /// <returns>A cached or newly created <see cref="ArcadeRacerDataSet"/>.</returns>
        public ArcadeRacerDataSet ArcadeRacer() =>
            ContextHelper.GetOrSet(faker, f => new ArcadeRacerDataSet(f));
    }
}
