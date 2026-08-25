// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using Json.Schema;
using Kurrent.Surge.Client.Processors.Partitioning;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Schema;
using JsonSchema = Json.Schema.JsonSchema;

namespace Kurrent.Surge.Schema.Validation;

/// <summary>
/// Parses and exports message schemas with JsonSchema.Net.
/// </summary>
/// <remarks>
/// Covers draft 6 and above. A draft-4 definition is refused while parsing.
/// Use <see cref="NJsonSchemaAgent"/> for draft-4.
/// </remarks>
[PublicAPI]
public class JsonSchemaNetAgent : ISchemaAgent {
    /// <summary>
    /// Gets a shared agent that uses the default evaluation options and exporter.
    /// </summary>
    public static readonly ISchemaAgent Instance = new JsonSchemaNetAgent();

    // JsonSchema.FromText writes the document into the library's process-wide schema registry, and
    // that registry is not thread safe in this version. Two threads building schemas at the same
    // time corrupt its backing dictionary, which surfaces as InvalidOperationException
    // ("non-concurrent collection ... corrupted its state") or JsonSchemaException ("Overwriting
    // registered schemas is not permitted"). Measured at 17-586 failures per 12 800 validations
    // across 16 threads; zero once this gate is in place.
    //
    // The gate is static because the registry it protects is static, and it covers only the build.
    // A cache hit never reaches here, so only the first parse of a given schema serialises.
    //
    // Fixed upstream in JsonSchema.Net 9.1.1, which is past the last MIT release. Remove this gate
    // if the package ever moves to 9.1.1 or later. See README.md.
    static readonly Lock SchemaRegistryGate = new();

    static JsonSchema BuildSchema(string definition) {
        lock (SchemaRegistryGate)
            return JsonSchema.FromText(definition);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchemaNetAgent"/> class.
    /// </summary>
    /// <param name="evaluationOptions">The options for evaluation. Defaults to list output, which is what carries the errors.</param>
    /// <param name="exporter">The exporter that turns a type into a schema definition.</param>
    public JsonSchemaNetAgent(EvaluationOptions? evaluationOptions = null, ISchemaExporter? exporter = null) {
        EvaluationOptions = evaluationOptions ?? new EvaluationOptions {
            // Flag is the default and reports only a pass/fail bit with no Details, which would
            // make every failed validation come back with an empty error list.
            OutputFormat = OutputFormat.List
        };

        Exporter   = exporter ?? SystemJsonSchemaExporter.Instance;
        Serializer = new();
    }

    EvaluationOptions EvaluationOptions { get; }
    ISchemaExporter   Exporter          { get; }
    JsonSerializer    Serializer        { get; }

    // The cache holds Lazy, not the schema itself. ConcurrentDictionary.GetOrAdd does not promise
    // to invoke its factory once per key, so two threads that miss the same entry would each build
    // the schema — and the second registration of an $id already in the registry is refused with
    // "Overwriting registered schemas is not permitted". ExecutionAndPublication makes the build
    // run exactly once per distinct schema.
    ConcurrentDictionary<uint, Lazy<MessageSchema>> ParsedSchemaCache    { get; } = new();
    ConcurrentDictionary<Type, Lazy<MessageSchema>> ExtractedSchemaCache { get; } = new();

    /// <inheritdoc/>
    /// <exception cref="JsonSchemaException">The definition is not a schema this agent can read, which includes any draft-4 document.</exception>
    public MessageSchema ParseSchema(ReadOnlySpan<char> schemaDefinition) {
        var definition = schemaDefinition.ToString();

        return ParsedSchemaCache.GetOrAdd(
            HashGenerators.FromString.XXHash(definition), CreateSchema(),
            (Definition: definition, EvaluationOptions, Serializer)
        ).Value;

        static Func<uint, (string Definition, EvaluationOptions EvaluationOptions, JsonSerializer Serializer), Lazy<MessageSchema>> CreateSchema() =>
            static (_, state) => new Lazy<MessageSchema>(
                () => new JsonSchemaNetMessageSchema(
                    state.Definition, BuildSchema(state.Definition),
                    state.EvaluationOptions,
                    state.Serializer
                ),
                LazyThreadSafetyMode.ExecutionAndPublication
            );
    }

    /// <inheritdoc/>
    public MessageSchema ExportSchema(Type type) {
        return ExtractedSchemaCache.GetOrAdd(type, CreateSchema(), (Exporter, EvaluationOptions, Serializer)).Value;

        static Func<Type, (ISchemaExporter Exporter, EvaluationOptions EvaluationOptions, JsonSerializer Serializer), Lazy<MessageSchema>> CreateSchema() =>
            static (type, state) => new Lazy<MessageSchema>(
                () => {
                    var definition = state.Exporter.Export(type, SchemaDataFormat.Json);

                    return new JsonSchemaNetMessageSchema(
                        definition, BuildSchema(definition),
                        state.EvaluationOptions,
                        state.Serializer
                    );
                },
                LazyThreadSafetyMode.ExecutionAndPublication
            );
    }
}
