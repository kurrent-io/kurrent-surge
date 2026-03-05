using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Transfermarkt.Domain;

namespace Kurrent.Client.Tests.Schema.Serialization.Fixtures;

[JsonSerializable(typeof(PlayerTransferred))]
[JsonSerializable(typeof(PlayerRetired))]
[JsonSerializable(typeof(PlayerRegistered))]
[JsonSerializable(typeof(LoanConvertedToPermanent))]
partial class TestJsonContext : JsonSerializerContext;
