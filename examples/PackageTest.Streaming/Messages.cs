using Kurrent.Schema.Catalogs;

// Two-hop analyzer-flow probe. The message-catalog generator ships inside Kurrent.Schema.Catalogs;
// this project references ONLY Kurrent.Surge.Streaming.Core, so the generator must reach it through
// Streaming -> Client -> Schema.Catalogs (NuGet does not flow analyzers transitively by default —
// each edge carries them only because of PrivateAssets="none"). This declaration drives the
// generator; Program.cs references the generated catalog so a broken flow fails the build.
[assembly: MessageCatalog("PackageTest.Streaming.Events", Key = "StreamingProbe")]

namespace PackageTest.Streaming.Events;

public record ProbePlaced(string Id);
