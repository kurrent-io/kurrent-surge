// Smoke test: verify the Kurrent.Streaming.Core package resolves correctly,
// including transitive access to Kurrent.Surge.Client and bundled Kurrent.Variant.
//
// Only Kurrent.Streaming.Core is referenced directly — everything else must
// flow transitively through the dependency graph.

using Kurrent.Surge.Client;
using Kurrent.Variant;

var streamingAsm = typeof(Kurrent.Streaming.KurrentProcessor).Assembly;
var clientAsm    = typeof(KurrentClient).Assembly;
var variantAsm   = typeof(IVariant).Assembly;

Console.WriteLine($"Kurrent.Streaming.Core: {streamingAsm.GetName().Name} v{streamingAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Client:    {clientAsm.GetName().Name} v{clientAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Variant:   {variantAsm.GetName().Name} v{variantAsm.GetName().Version}");

// Two-hop generator-flow probe: the catalog generator (shipped in Kurrent.Schema.Catalogs) must
// reach this Streaming-only consumer. Referencing the generated catalog fails the build if it didn't.
var generatedCatalogs = PackageTest.Streaming.MessageCatalogs.All;
Console.WriteLine($"Generated catalogs flowed two hops: {generatedCatalogs.Count}");

Console.WriteLine("Streaming package smoke test passed.");
