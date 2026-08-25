// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;

namespace Kurrent.Surge.Schema.Validation.Tests;

/// <summary>
/// The agent is reached as a shared singleton, and the library's schema registry is process-wide and
/// not thread safe in the pinned version. `JsonSchemaNetAgent` guards the build with a lock and
/// caches `Lazy` so a schema is built once. Remove either and these turn red.
/// </summary>
/// <remarks>
/// See the project README for the measurements behind this. Both mitigations come out when the
/// package moves to JsonSchema.Net 9.1.1 or later, and these tests stay.
/// </remarks>
[NotInParallel("schema-registry")]
[Timeout(60_000)]
public class ConcurrentAccessTests {
    const int Threads   = 16;
    const int PerThread = 200;

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask many_threads_parsing_and_validating_never_fail() {
        // Arrange — a $id forces registration and a $ref into $defs forces a registry lookup on
        // every evaluation. Half the iterations reuse a small shared set, which races the cache;
        // half introduce a schema no thread has seen, which races registration.
        var run      = Faker.Random.Guid().ToString("N");
        var agent    = JsonSchemaNetAgent.Instance;
        var failures = new ConcurrentBag<string>();
        var barrier  = new Barrier(Threads);

        // Act
        var threads = Enumerable.Range(0, Threads).Select(index => {
            var thread = new Thread(() => {
                barrier.SignalAndWait();

                for (var i = 0; i < PerThread; i++) {
                    var id = i % 2 == 0 ? $"{run}-shared-{i % 8}" : $"{run}-unique-{index}-{i}";

                    try {
                        var schema = agent.ParseSchema(Schema(id));

                        if (!schema.Validate(Conforming).IsValid)
                            failures.Add("valid payload was rejected");

                        if (schema.Validate(Breaking).IsValid)
                            failures.Add("breaking payload was accepted");
                    }
                    catch (Exception ex) {
                        failures.Add($"{ex.GetType().Name}: {ex.Message}");
                    }
                }
            });

            thread.Start();
            return thread;
        }).ToList();

        foreach (var thread in threads)
            thread.Join();

        // Assert
        await Assert.That(failures).IsEmpty().Because(Summarise(failures));
    }

    [Test]
    public async ValueTask many_threads_exporting_the_same_type_agree_on_one_definition() {
        // Arrange — ExportSchema caches by type, so every thread has to observe the same instance
        var definitions = new ConcurrentBag<string>();
        var barrier     = new Barrier(Threads);

        // Act
        var threads = Enumerable.Range(0, Threads).Select(_ => {
            var thread = new Thread(() => {
                barrier.SignalAndWait();

                for (var i = 0; i < PerThread; i++)
                    definitions.Add(JsonSchemaNetAgent.Instance.ExportSchema<Exported>().Definition);
            });

            thread.Start();
            return thread;
        }).ToList();

        foreach (var thread in threads)
            thread.Join();

        // Assert
        await Assert.That(definitions.Distinct().Count()).IsEqualTo(1);
    }

    public sealed record Exported {
        public required string Id       { get; init; }
        public required int    Quantity { get; init; }
    }

    const string Conforming = """{"id":"a","total":{"amount":1}}""";
    const string Breaking   = """{"id":"a","total":{}}""";

    static string Schema(string id) => $$"""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://kurrent.test/{{id}}",
      "type": "object",
      "properties": { "id": { "type": "string" }, "total": { "$ref": "#/$defs/Money" } },
      "required": [ "id", "total" ],
      "$defs": {
        "Money": {
          "type": "object",
          "properties": { "amount": { "type": "integer" } },
          "required": [ "amount" ]
        }
      }
    }
    """;

    static string Summarise(ConcurrentBag<string> failures) =>
        failures.IsEmpty
            ? ""
            : $"{failures.Count} failures: " + string.Join(" | ", failures.GroupBy(f => f).Select(g => $"[{g.Count()}x] {g.Key}").Take(4));
}
