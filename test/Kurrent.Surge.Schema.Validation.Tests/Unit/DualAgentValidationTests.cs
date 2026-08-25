// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

public class DualAgentValidationTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask both_agents_accept_a_conforming_payload() {
        // Arrange
        var payload = Payload(id: Faker.Random.Guid().ToString(), quantity: Faker.Random.Int(1, 100));

        // Act
        var nJsonSchema    = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);
        var jsonSchemaNet  = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsTrue();
        await Assert.That(jsonSchemaNet.IsValid).IsTrue();
    }

    [Test]
    public async ValueTask both_agents_reject_a_wrongly_typed_property() {
        // Arrange — quantity is declared integer, so a string breaks the contract
        var payload = $$"""{"id":"{{Faker.Random.Guid()}}","quantity":"{{Faker.Random.Word()}}"}""";

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsFalse();
        await Assert.That(jsonSchemaNet.IsValid).IsFalse();

        await Assert.That(nJsonSchema.Errors.Any(error => error.Path?.Contains("quantity") == true)).IsTrue();
        await Assert.That(jsonSchemaNet.Errors.Any(error => error.Path == "/quantity")).IsTrue();
    }

    [Test]
    public async ValueTask both_agents_reject_a_missing_required_property() {
        // Arrange — "id" is required and absent
        var payload = $$"""{"quantity":{{Faker.Random.Int(1, 100)}}}""";

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsFalse();
        await Assert.That(jsonSchemaNet.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask both_agents_reject_a_wrongly_typed_array_item() {
        // Arrange — tags is an array of strings
        var payload = $$"""{"id":"{{Faker.Random.Guid()}}","quantity":{{Faker.Random.Int(1, 100)}},"tags":[1]}""";

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(payload);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsFalse();
        await Assert.That(jsonSchemaNet.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask both_agents_validate_the_same_payload_from_utf8_bytes() {
        // Arrange — AsSpan is required. A byte[] binds to Validate<T> by identity conversion, which
        // beats the conversion to ReadOnlySpan<byte>, and the array is then serialized to base64
        // rather than read as JSON.
        var payload = Payload(id: Faker.Random.Guid().ToString(), quantity: Faker.Random.Int(1, 100));
        var bytes   = System.Text.Encoding.UTF8.GetBytes(payload);

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(bytes.AsSpan());
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(bytes.AsSpan());

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsTrue().Because(Errors(nJsonSchema));
        await Assert.That(jsonSchemaNet.IsValid).IsTrue().Because(Errors(jsonSchemaNet));
    }

    [Test]
    public async ValueTask both_agents_reject_a_broken_payload_from_utf8_bytes() {
        // Arrange — a passing byte-path test proves nothing unless the same path also rejects
        var bytes = System.Text.Encoding.UTF8.GetBytes($$"""{"quantity":{{Faker.Random.Int(1, 100)}}}""");

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(bytes.AsSpan());
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral).Validate(bytes.AsSpan());

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsFalse();
        await Assert.That(jsonSchemaNet.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask both_agents_reject_empty_byte_input() {
        // Arrange
        var nJsonSchema   = NJsonSchemaAgent.Instance.ParseSchema(Schemas.DialectNeutral);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ParseSchema(Schemas.DialectNeutral);

        // Act
        var validateWithNJsonSchema   = () => nJsonSchema.Validate(ReadOnlySpan<byte>.Empty);
        var validateWithJsonSchemaNet = () => jsonSchemaNet.Validate(ReadOnlySpan<byte>.Empty);

        // Assert
        await Assert.That(validateWithNJsonSchema).Throws<InvalidOperationException>();
        await Assert.That(validateWithJsonSchemaNet).Throws<InvalidOperationException>();
    }

    static string Payload(string id, int quantity) =>
        $$"""{"id":"{{id}}","quantity":{{quantity}},"tags":["a","b"]}""";

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
