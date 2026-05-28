// Smoke test: verify the Kurrent.Streaming package resolves correctly,
// including transitive access to Kurrent.Client and bundled Kurrent.Variant.
//
// Only Kurrent.Streaming is referenced directly — everything else must
// flow transitively through the dependency graph.

using Kurrent.Client;
using Kurrent.Variant;

var streamingAsm = typeof(Kurrent.Streaming.KurrentProcessor).Assembly;
var clientAsm    = typeof(KurrentClient).Assembly;
var variantAsm   = typeof(IVariant).Assembly;

Console.WriteLine($"Kurrent.Streaming: {streamingAsm.GetName().Name} v{streamingAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Client:    {clientAsm.GetName().Name} v{clientAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Variant:   {variantAsm.GetName().Name} v{variantAsm.GetName().Version}");
Console.WriteLine("Streaming package smoke test passed.");
