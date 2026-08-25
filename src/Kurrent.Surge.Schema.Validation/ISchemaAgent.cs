// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.Schema.Validation;


/// <summary>
/// Defines an agent capable of parsing and exporting message schemas.
/// </summary>
public interface ISchemaAgent
{
    /// <summary>
    /// Parses a schema definition from a ReadOnlySpan of characters.
    /// </summary>
    /// <param name="schemaDefinition">The schema definition as a ReadOnlySpan of characters.</param>
    /// <returns>A MessageSchema representing the parsed schema.</returns>
    MessageSchema ParseSchema(ReadOnlySpan<char> schemaDefinition);

    /// <summary>
    /// Exports a message schema based on the provided Type.
    /// </summary>
    /// <param name="type">The Type for which to export the schema.</param>
    /// <returns>A MessageSchema representing the exported schema.</returns>
    MessageSchema ExportSchema(Type type);
}

/// <summary>
/// Represents a parsed message schema that validates data and compares against another schema.
/// </summary>
/// <param name="schemaDefinition">The schema definition this instance was built from.</param>
public abstract class MessageSchema(string schemaDefinition) {
    /// <summary>
    /// Gets the schema definition this instance was built from.
    /// </summary>
    public string Definition { get; } = schemaDefinition;

    /// <summary>
    /// Validates JSON text against this schema.
    /// </summary>
    /// <param name="data">The JSON text to validate.</param>
    /// <returns>The result of the validation, carrying every error found.</returns>
    public abstract SchemaValidationResult Validate(string data);

    /// <summary>
    /// Validates UTF-8 encoded JSON against this schema.
    /// </summary>
    /// <param name="data">The UTF-8 encoded JSON to validate.</param>
    /// <returns>The result of the validation, carrying every error found.</returns>
    /// <remarks>
    /// A <see cref="byte"/> array binds to the generic overload instead of this one. Pass a span to
    /// reach this method, otherwise the array is serialized rather than read as JSON.
    /// </remarks>
    public abstract SchemaValidationResult Validate(ReadOnlySpan<byte> data);

    /// <summary>
    /// Serializes an instance and validates the result against this schema.
    /// </summary>
    /// <typeparam name="T">The type of the instance.</typeparam>
    /// <param name="data">The instance to validate.</param>
    /// <returns>The result of the validation, carrying every error found.</returns>
    public abstract SchemaValidationResult Validate<T>(T data) where T : class;

    /// <summary>
    /// Checks this schema against an earlier one under the given compatibility mode.
    /// </summary>
    /// <param name="schema">The reference schema to check against. This instance is the schema under test.</param>
    /// <param name="mode">The compatibility mode to enforce.</param>
    public abstract SchemaCompatibilityResult IsCompatible(MessageSchema schema, SchemaCompatibilityMode mode);

    public override string ToString() => Definition;
}

public static class SchemaAgentExtensions {
    public static MessageSchema ExportSchema<T>(this ISchemaAgent agent) =>
        agent.ExportSchema(typeof(T));

    public static SchemaValidationResult Validate(this ISchemaAgent agent, ReadOnlySpan<byte> data, string schemaDefinition) =>
        agent.ParseSchema(schemaDefinition).Validate(data);
}

public static class MessageSchemaExtensions {
    public static SchemaValidationResult Validate(this MessageSchema schema, ReadOnlySpan<char> data) =>
        schema.Validate(data.ToString());

    public static SchemaValidationResult Validate(this MessageSchema schema, JsonDocument data) =>
        schema.Validate(data.ToString() ?? "");

    public static SchemaValidationResult Validate(this MessageSchema schema, JsonNode data) =>
        schema.Validate(data.ToJsonString());
}