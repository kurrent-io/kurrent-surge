using System.Text.Json.Serialization;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Surge.Tests.Codecs.Fixtures;

[JsonSerializable(typeof(LapCompleted))]
[JsonSerializable(typeof(OvertakePerformed))]
[JsonSerializable(typeof(PitStopPerformed))]
partial class TestJsonContext : JsonSerializerContext;
