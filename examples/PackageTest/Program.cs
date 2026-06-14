// Smoke test: verify Kurrent packages resolve correctly, including
// bundled Kurrent.Variant types that flow transitively via Bundle="true".
//
// Default (ProjectReference):
//   dotnet run --project examples/PackageTest
//
// Package mode (PackageReference from local feed):
//   python3 scripts/verify-package.py --csproj src/Kurrent.Client/Kurrent.Client.csproj --output-dir .artifacts/local-packages
//   python3 scripts/verify-package.py --csproj src/Kurrent.Streaming/Kurrent.Streaming.csproj --output-dir .artifacts/local-packages
//   dotnet run --project examples/PackageTest -p:TestPackages=true

using Kurrent.Client;
using Kurrent.Variant;

var clientAsm    = typeof(KurrentClient).Assembly;
var streamingAsm = typeof(Kurrent.Streaming.KurrentProcessor).Assembly;
var variantAsm   = typeof(IVariant).Assembly;

var catalog = new MessageCatalog("Test");

Console.WriteLine($"Kurrent.Client:    {clientAsm.GetName().Name} v{clientAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Streaming: {streamingAsm.GetName().Name} v{streamingAsm.GetName().Version}");
Console.WriteLine($"Kurrent.Variant:   {variantAsm.GetName().Name} v{variantAsm.GetName().Version}");
Console.WriteLine("Package smoke test passed.");
