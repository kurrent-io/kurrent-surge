using System.Text.Json;

namespace ProtobufTranscoder.Tests;

/// <summary>
/// Bare minimum tests proving the redesigned API surface works.
/// </summary>
public class ApiRedesignTests {
    // Simple proto3 message: { name: "Bob", age: 25, active: true }
    const string SimpleProto = """
        syntax = "proto3";
        package test;
        message Person {
          string name = 1;
          int32 age = 2;
          bool active = 3;
        }
        """;

    static readonly byte[] PersonBytes = [
        0x0A,
        0x03,
        0x42,
        0x6F,
        0x62, // name = "Bob"
        0x10,
        0x19, // age = 25
        0x18,
        0x01 // active = true
    ];

    // ── ProtoDefinitions.Build ───────────────────────────────────────────

    [Test]
    public async Task ProtoDefinitions_Build_FromProtoSchema() {
        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", SimpleProto));

        var descriptor = defs.FindMessageType("test.Person");
        await Assert.That(descriptor.FullName).IsEqualTo("test.Person");
        await Assert.That(descriptor.Fields.Count).IsEqualTo(3);
    }

    // ── ProtoTranscode (pure static functions) ──────────────────────────

    [Test]
    public async Task ProtoTranscode_ToDynamicMessage() {
        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", SimpleProto));

        var message = ProtoTranscode.ToDynamicMessage(PersonBytes, defs, "test.Person");

        var nameField = message.Descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(nameField)).IsEqualTo("Bob");
    }

    [Test]
    public async Task ProtoTranscode_ToJson() {
        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", SimpleProto));

        var json = ProtoTranscode.ToJson(PersonBytes, defs, "test.Person");

        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Bob");
        await Assert.That(doc.RootElement.GetProperty("age").GetInt32()).IsEqualTo(25);
    }

    [Test]
    public async Task ProtoTranscode_ToJsonDocument() {
        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", SimpleProto));

        using var doc = ProtoTranscode.ToJsonDocument(PersonBytes, defs, "test.Person");

        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Bob");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task ProtoTranscode_ToJsonNode() {
        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", SimpleProto));

        var node = ProtoTranscode.ToJsonNode(PersonBytes, defs, "test.Person");

        await Assert.That(node["name"]!.GetValue<string>()).IsEqualTo("Bob");
        await Assert.That(node["age"]!.GetValue<int>()).IsEqualTo(25);
        await Assert.That(node["active"]!.GetValue<bool>()).IsTrue();
    }

    // ── ProtoTranscoder (with IProtoSchemaSource) ───────────────────────

    [Test]
    public async Task ProtoTranscoder_ToJsonNode_ResolvesAndDecodes() {
        var source = new FakeSchemaSource(
            "person-v1", "test.Person", SimpleProto,
            "1.0");

        var transcoder = new ProtoTranscoder(source);

        var node = transcoder.ToJsonNode(PersonBytes, "person-v1");

        await Assert.That(node["name"]!.GetValue<string>()).IsEqualTo("Bob");
        await Assert.That(node["age"]!.GetValue<int>()).IsEqualTo(25);
    }

    [Test]
    public async Task ProtoTranscoder_CacheHit_DoesNotRebuild() {
        var source = new FakeSchemaSource(
            "person-v1", "test.Person", SimpleProto,
            "1.0");

        var transcoder = new ProtoTranscoder(source);

        // First call — cold cache, builds definitions
        transcoder.ToJsonNode(PersonBytes, "person-v1");
        var buildCountAfterFirst = source.ResolveCount;

        // Second call — cache hit, still calls Resolve but does NOT rebuild
        transcoder.ToJsonNode(PersonBytes, "person-v1");

        // Resolve is called each time (to check version), but build count stays same
        await Assert.That(source.ResolveCount).IsEqualTo(2);
        await Assert.That(source.BuildCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProtoTranscoder_VersionChange_RebuildsCachedDefinitions() {
        var source = new FakeSchemaSource(
            "person-v1", "test.Person", SimpleProto,
            "1.0");

        var transcoder = new ProtoTranscoder(source);

        // First call — builds
        transcoder.ToJsonNode(PersonBytes, "person-v1");
        await Assert.That(source.BuildCount).IsEqualTo(1);

        // Version changes
        source.Version = "2.0";

        // Second call — version mismatch, rebuilds
        transcoder.ToJsonNode(PersonBytes, "person-v1");
        await Assert.That(source.BuildCount).IsEqualTo(2);
    }

    // ── FileSystemSchemaResolver ────────────────────────────────────────

    [Test]
    public async Task FileSystemSchemaResolver_ResolvesFromDisk() {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try {
            var protoPath = Path.Combine(tempDir, "test.proto");
            File.WriteAllText(protoPath, SimpleProto);

            var resolver = new FileSystemSchemaResolver(tempDir);
            var content  = resolver.Resolve("test.proto");

            await Assert.That(content).IsEqualTo(SimpleProto);
        }
        finally {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task FileSystemSchemaResolver_ReturnsNull_ForMissingFile() {
        var resolver = new FileSystemSchemaResolver(Path.GetTempPath());

        var result = resolver.Resolve("nonexistent.proto");

        await Assert.That(result).IsNull();
    }

    // ── Test helper ─────────────────────────────────────────────────────

    class FakeSchemaSource(
        string schemaId,
        string messageTypeName,
        string protoContent,
        string version
    ) : IProtoSchemaSource {
        string?       _lastBuiltVersion;
        public int    ResolveCount { get; private set; }
        public int    BuildCount   { get; private set; }
        public string Version      { get; set; } = version;

        public ProtoSchemaInfo Resolve(string id) {
            if (id != schemaId)
                throw new ArgumentException($"Unknown schema: {id}");

            ResolveCount++;

            // Track whether a build would be needed
            if (_lastBuiltVersion != Version) {
                BuildCount++;
                _lastBuiltVersion = Version;
            }

            return new ProtoSchemaInfo(
                messageTypeName,
                [new ProtoSchema("test.proto", protoContent)],
                Version);
        }
    }
}