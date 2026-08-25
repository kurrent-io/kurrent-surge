// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Json.Schema;
using JsonSchema = Json.Schema.JsonSchema;
using JsonSerializer = Kurrent.Surge.Codecs.Json.JsonSerializer;

namespace Kurrent.Surge.Schema.Validation;

/// <summary>
/// A message schema parsed by JsonSchema.Net.
/// </summary>
/// <param name="schemaDefinition">The schema definition this instance was built from.</param>
/// <param name="schema">The parsed schema.</param>
/// <param name="evaluationOptions">The options for evaluation.</param>
/// <param name="serializer">The serializer that turns an instance into the JSON to validate.</param>
[PublicAPI]
public class JsonSchemaNetMessageSchema(
    string schemaDefinition, JsonSchema schema, EvaluationOptions evaluationOptions, JsonSerializer serializer
) : MessageSchema(schemaDefinition) {
    internal JsonSchema Schema { get; } = schema;

    EvaluationOptions EvaluationOptions { get; } = evaluationOptions;
    JsonSerializer    Serializer        { get; } = serializer;

    /// <inheritdoc/>
    /// <exception cref="SchemaValidationException">The data is not readable as JSON, or evaluation failed.</exception>
    public override SchemaValidationResult Validate(string data) {
        try {
            using var document = JsonDocument.Parse(data);
            return Evaluate(document.RootElement);
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException"><paramref name="data"/> is empty.</exception>
    /// <exception cref="SchemaValidationException">The data is not readable as JSON, or evaluation failed.</exception>
    public override SchemaValidationResult Validate(ReadOnlySpan<byte> data) {
        if (data.IsEmpty)
            throw new InvalidOperationException("Data cannot be empty.");

        try {
            var       reader   = new Utf8JsonReader(data);
            using var document = JsonDocument.ParseValue(ref reader);
            return Evaluate(document.RootElement);
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }

    /// <inheritdoc/>
    public override SchemaValidationResult Validate<T>(T data) where T : class =>
        Validate(Serializer.Serialize(data).Span);

    /// <inheritdoc/>
    public override SchemaCompatibilityResult IsCompatible(MessageSchema other, SchemaCompatibilityMode mode) =>
        JsonSchemaNetCompatibilityManager.CheckCompatibility(Definition, [other.Definition], mode);

    SchemaValidationResult Evaluate(JsonElement instance) {
        var results = Schema.Evaluate(instance, EvaluationOptions);

        // The verdict has to come from IsValid, never from the error count. A keyword that fails
        // inside a branch of anyOf/oneOf still reports its error even when a sibling branch
        // succeeds and the whole evaluation passes.
        return results.IsValid
            ? SchemaValidationResult.Success()
            : SchemaValidationResult.Failure(results.Map());
    }
}
