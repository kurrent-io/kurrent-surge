using System.Text.Json.Serialization;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Client.Tests.Schema.Serialization.Fixtures;

[JsonSerializable(typeof(LapCompleted))]
[JsonSerializable(typeof(OvertakePerformed))]
[JsonSerializable(typeof(PitStopPerformed))]
partial class TestJsonContext : JsonSerializerContext;
