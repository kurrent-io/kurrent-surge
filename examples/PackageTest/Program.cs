// Smoke test: verify Kurrent packages resolve correctly, including
// bundled Kurrent.Variant types that flow transitively via Bundle="true".
//
// Default (ProjectReference):
//   dotnet run --project examples/PackageTest
//
// Package mode (PackageReference from local feed):
//   python3 scripts/verify-package.py --csproj src/Kurrent.Surge.Client/Kurrent.Surge.Client.csproj --output-dir .artifacts/local-packages
//   python3 scripts/verify-package.py --csproj src/Kurrent.Surge.Streaming.Core/Kurrent.Surge.Streaming.Core.csproj --output-dir .artifacts/local-packages
//   dotnet run --project examples/PackageTest -p:TestPackages=true

using Kurrent.Surge.Client;
using Kurrent.Schema.Catalogs;
using Kurrent.Variant;

var clientAsm    = typeof(KurrentClient).Assembly;
var streamingAsm = typeof(Kurrent.Surge.Streaming.KurrentProcessor).Assembly;
var variantAsm   = typeof(IVariant).Assembly;

var catalog = new MessageCatalog("Test");

Console.WriteLine($"Kurrent.Surge.Client:    {clientAsm.GetName().Name} v{clientAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Surge.Streaming.Core: {streamingAsm.GetName().Name} v{streamingAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Variant:   {variantAsm.GetName().Name} v{variantAsm.GetName().Version}");
Console.WriteLine("Package smoke test passed.");
