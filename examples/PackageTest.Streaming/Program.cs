// Smoke test: verify the Kurrent.Surge package resolves correctly, including bundled
// Kurrent.Variant, and that the message-catalog generator reaches a consumer that
// references only Kurrent.Surge (the generator ships in Kurrent.Surge.Schema.Catalogs
// and must flow transitively through the dependency graph).

using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;

// Touch a type from every referenced package so its assembly must load — proving each one resolved.
var surge    = typeof(KurrentClient).Assembly;    // Kurrent.Surge (impl: client + streaming)
var core     = typeof(IKurrentClient).Assembly;   // Kurrent.Surge.Core (contracts)
var catalogs = typeof(MessageCatalog).Assembly;   // Kurrent.Surge.Schema.Catalogs
var variant  = typeof(IVariant).Assembly;         // Kurrent.Variant (bundled via Kurrent.Surge.Core)
_ = typeof(Kurrent.Surge.Processors.KurrentProcessor);   // streaming surface (also Kurrent.Surge)

Console.WriteLine($"Kurrent.Surge                 -> {surge.GetName().Name} v{surge.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Core            -> {core.GetName().Name} v{core.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Schema.Catalogs -> {catalogs.GetName().Name} v{catalogs.GetName().Version}");
Console.WriteLine($"Kurrent.Variant               -> {variant.GetName().Name} v{variant.GetName().Version}");

// Generator-flow probe: the catalog generator (shipped in Kurrent.Surge.Schema.Catalogs) must reach
// this Kurrent.Surge-only consumer. Referencing the generated catalog fails the build if it didn't.
var generatedCatalogs = PackageTest.Streaming.MessageCatalogs.All;
Console.WriteLine($"Generated catalogs flowed two hops: {generatedCatalogs.Count}");

Console.WriteLine("Streaming package smoke test passed.");
