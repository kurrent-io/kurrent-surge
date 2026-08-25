// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Json.Schema;

namespace Kurrent.Surge.Schema.Validation;

/// <summary>
/// Extensions for <see cref="EvaluationResults"/>.
/// </summary>
static class JsonSchemaNetMappings {
    extension(EvaluationResults results) {
        /// <summary>
        /// Flattens an evaluation tree into the validation errors it reports.
        /// </summary>
        public List<SchemaValidationError> Map() {
            var errors = new List<SchemaValidationError>();
            Collect(results, errors);
            return errors;
        }
    }

    // The evaluation tree carries one node per applied keyword, and an applicator restates its
    // children's failure in its own terms:
    //   properties -> "Some properties did not match the required schema: [\"id\",\"total\"]"
    //   /id        -> "Value is \"integer\" but should be \"string\""
    // Reporting both says the same thing twice, so a node is dropped when a descendant already
    // reported. Later releases expose EvaluationOptions.IncludeApplicatorErrors for this, but that
    // option arrived after the last MIT version this package is pinned to.
    // Returns whether this subtree reported anything.
    static bool Collect(EvaluationResults node, List<SchemaValidationError> errors) {
        var reportedBelow = false;

        foreach (var detail in node.Details ?? [])
            reportedBelow |= Collect(detail, errors);

        if (reportedBelow || node.Errors is not { Count: > 0 })
            return reportedBelow;

        var path = node.InstanceLocation.ToString();

        foreach (var (_, message) in node.Errors)
            errors.Add(
                new SchemaValidationError {
                    ErrorMessage = message,
                    Property     = PropertyName(path),
                    Path         = path
                    // LineInfo stays null: the evaluator reports JSON Pointers, never offsets.
                }
            );

        return true;
    }

    // Instance locations are JSON Pointers: "" is the document root, "/total/amount" names a
    // nested member, and a member name containing '/' or '~' arrives escaped as "~1" or "~0".
    // Unescape in that order, otherwise a literal "~1" decodes to '/'.
    // See https://datatracker.ietf.org/doc/html/rfc6901#section-3
    static string? PropertyName(string pointer) {
        if (pointer.Length == 0)
            return null;

        var segment = pointer[(pointer.LastIndexOf('/') + 1)..];

        return segment.Replace("~1", "/").Replace("~0", "~");
    }
}
