using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Tests;

public class FeatureSetTests {
    [Test]
    public async Task Proto2Defaults_AreCorrect() {
        var features = FeatureSet.DefaultForProto2;

        await Assert.That(features.FieldPresence).IsEqualTo(FieldPresence.Explicit);
        await Assert.That(features.EnumType).IsEqualTo(EnumType.Closed);
        await Assert.That(features.RepeatedFieldEncoding).IsEqualTo(RepeatedFieldEncoding.Expanded);
        await Assert.That(features.MessageEncoding).IsEqualTo(MessageEncoding.LengthPrefixed);
        await Assert.That(features.JsonFormat).IsEqualTo(JsonFormat.LegacyBestEffort);
    }

    [Test]
    public async Task Proto3Defaults_AreCorrect() {
        var features = FeatureSet.DefaultForProto3;

        await Assert.That(features.FieldPresence).IsEqualTo(FieldPresence.Implicit);
        await Assert.That(features.EnumType).IsEqualTo(EnumType.Open);
        await Assert.That(features.RepeatedFieldEncoding).IsEqualTo(RepeatedFieldEncoding.Packed);
        await Assert.That(features.MessageEncoding).IsEqualTo(MessageEncoding.LengthPrefixed);
        await Assert.That(features.JsonFormat).IsEqualTo(JsonFormat.Allow);
    }

    [Test]
    public async Task Editions2024Defaults_AreCorrect() {
        var features = FeatureSet.DefaultForEditions2024;

        await Assert.That(features.FieldPresence).IsEqualTo(FieldPresence.Explicit);
        await Assert.That(features.EnumType).IsEqualTo(EnumType.Open);
        await Assert.That(features.RepeatedFieldEncoding).IsEqualTo(RepeatedFieldEncoding.Packed);
        await Assert.That(features.MessageEncoding).IsEqualTo(MessageEncoding.LengthPrefixed);
        await Assert.That(features.JsonFormat).IsEqualTo(JsonFormat.Allow);
    }

    [Test]
    public async Task Resolve_WithNullOverrides_ReturnsParent() {
        var parent   = FeatureSet.DefaultForProto3;
        var resolved = FeatureSet.Resolve(parent, null);

        await Assert.That(resolved.FieldPresence).IsEqualTo(FieldPresence.Implicit);
        await Assert.That(resolved.EnumType).IsEqualTo(EnumType.Open);
    }

    [Test]
    public async Task Resolve_WithPartialOverrides_MergesCorrectly() {
        var parent    = FeatureSet.DefaultForEditions2024;
        var overrides = new FeatureSetOverrides { FieldPresence = FieldPresence.Implicit };

        var resolved = FeatureSet.Resolve(parent, overrides);

        await Assert.That(resolved.FieldPresence).IsEqualTo(FieldPresence.Implicit);
        await Assert.That(resolved.EnumType).IsEqualTo(EnumType.Open);                             // inherited
        await Assert.That(resolved.RepeatedFieldEncoding).IsEqualTo(RepeatedFieldEncoding.Packed); // inherited
    }

    [Test]
    public async Task GetDefault_ReturnsCorrectForSyntax() {
        var p2 = FeatureSet.GetDefault("proto2");
        var p3 = FeatureSet.GetDefault("proto3");
        var ed = FeatureSet.GetDefault("editions");

        await Assert.That(p2.FieldPresence).IsEqualTo(FieldPresence.Explicit);
        await Assert.That(p3.FieldPresence).IsEqualTo(FieldPresence.Implicit);
        await Assert.That(ed.FieldPresence).IsEqualTo(FieldPresence.Explicit);
    }
}