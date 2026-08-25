// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema.Validation.Tests;

/// The other half of ISchemaAgent. Each agent exports through its own generator, so the documents
/// differ; what has to hold is that each one describes the type it was given.
public class SchemaExportTests {
    public sealed record Order {
        public required string Id       { get; init; }
        public required int    Quantity { get; init; }
        public string?         Note     { get; init; }
    }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask both_agents_export_a_schema_that_accepts_an_instance_of_the_type() {
        // Arrange — Validate<T> serializes through the same conventions the exporter reads, so the
        // instance and the exported document agree on how a property is named
        var instance = NewOrder();

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ExportSchema<Order>().Validate(instance);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ExportSchema<Order>().Validate(instance);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsTrue().Because(Errors(nJsonSchema));
        await Assert.That(jsonSchemaNet.IsValid).IsTrue().Because(Errors(jsonSchemaNet));
    }

    [Test]
    public async ValueTask both_agents_export_a_schema_that_rejects_a_wrongly_typed_payload() {
        // Arrange — the exported documents name the members in camel case, and Quantity is declared
        // an integer, so a string has to fail
        var json = $$$"""{"id":"{{{Faker.Random.Guid()}}}","quantity":"{{{Faker.Random.Word()}}}"}""";

        // Act
        var nJsonSchema   = NJsonSchemaAgent.Instance.ExportSchema<Order>().Validate(json);
        var jsonSchemaNet = JsonSchemaNetAgent.Instance.ExportSchema<Order>().Validate(json);

        // Assert
        await Assert.That(nJsonSchema.IsValid).IsFalse();
        await Assert.That(jsonSchemaNet.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask exporting_the_same_type_twice_returns_the_same_definition() {
        // Arrange
        var expected = JsonSchemaNetAgent.Instance.ExportSchema<Order>().Definition;

        // Act
        var actual = JsonSchemaNetAgent.Instance.ExportSchema<Order>().Definition;

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    Order NewOrder() => new() {
        Id       = Faker.Random.Guid().ToString(),
        Quantity = Faker.Random.Int(1, 100),
        Note     = Faker.Commerce.ProductName()
    };

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
