// Smoke test: verify Kurrent packages resolve correctly, including
// bundled Kurrent.Variant types that flow transitively via Bundle="true".
//
// Default (ProjectReference):
//   dotnet run --project examples/PackageTest
//
// Package mode (PackageReference from local feed — pack the deps first, then Kurrent.Surge):
//   python3 scripts/verify-package.py --csproj src/Kurrent.Surge.Core/Kurrent.Surge.Core.csproj --output-dir .artifacts/local-packages
//   python3 scripts/verify-package.py --csproj src/Kurrent.Surge.Schema.Catalogs/Kurrent.Surge.Schema.Catalogs.csproj --output-dir .artifacts/local-packages
//   python3 scripts/verify-package.py --csproj src/Kurrent.Surge/Kurrent.Surge.csproj --output-dir .artifacts/local-packages
//   dotnet run --project examples/PackageTest -p:TestPackages=true

using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Variant;

// Touch a type from every referenced package so its assembly must load — proving each one resolved.
var surge    = typeof(KurrentClient).Assembly;    // Kurrent.Surge (impl: client + streaming)
var core     = typeof(IKurrentClient).Assembly;   // Kurrent.Surge.Core (contracts)
var catalogs = typeof(MessageCatalog).Assembly;   // Kurrent.Surge.Schema.Catalogs
var variant  = typeof(IVariant).Assembly;         // Kurrent.Variant (bundled via Kurrent.Surge.Core)

// The streaming API also lives in Kurrent.Surge — touch it so a missing streaming surface fails here too.
_ = typeof(Kurrent.Surge.Processors.KurrentProcessor);

Console.WriteLine($"Kurrent.Surge                 -> {surge.GetName().Name} v{surge.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Core            -> {core.GetName().Name} v{core.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Schema.Catalogs -> {catalogs.GetName().Name} v{catalogs.GetName().Version}");
Console.WriteLine($"Kurrent.Variant               -> {variant.GetName().Name} v{variant.GetName().Version}");
Console.WriteLine("Package smoke test passed.");
