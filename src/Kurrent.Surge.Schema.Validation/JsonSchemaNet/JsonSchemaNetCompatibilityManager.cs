// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;

namespace Kurrent.Surge.Schema.Validation;

/// <summary>
/// Checks schema compatibility for schemas parsed by <see cref="JsonSchemaNetAgent"/>.
/// </summary>
[PublicAPI]
public class JsonSchemaNetCompatibilityManager : SchemaCompatibilityManagerBase {
    /// <inheritdoc/>
    protected override ValueTask<SchemaCompatibilityResult> CheckCompatibilityCore(
        string uncheckedSchema,
        string[] referenceSchemas,
        SchemaCompatibilityMode compatibility,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(CheckCompatibility(uncheckedSchema, referenceSchemas, compatibility));

    internal static SchemaCompatibilityResult CheckCompatibility(
        string uncheckedSchema, string[] referenceSchemas, SchemaCompatibilityMode compatibility
    ) {
        if (compatibility is SchemaCompatibilityMode.None)
            return SchemaCompatibilityResult.Compatible();

        if (compatibility is SchemaCompatibilityMode.Unspecified)
            throw new ArgumentException("Unspecified compatibility mode", nameof(compatibility));

        using var uncheckedDocument = JsonDocument.Parse(uncheckedSchema);

        var documents = new List<JsonDocument>(referenceSchemas.Length);

        try {
            foreach (var referenceSchema in referenceSchemas)
                documents.Add(JsonDocument.Parse(referenceSchema));

            var unchecked_ = uncheckedDocument.RootElement;
            var errors     = new List<SchemaCompatibilityError>();

            switch (compatibility) {
                case SchemaCompatibilityMode.Backward:
                case SchemaCompatibilityMode.BackwardAll:
                    foreach (var document in documents)
                        CheckBackward(document.RootElement, unchecked_, errors);

                    break;

                case SchemaCompatibilityMode.Forward:
                case SchemaCompatibilityMode.ForwardAll:
                    foreach (var document in documents)
                        CheckForward(document.RootElement, unchecked_, errors);

                    break;

                case SchemaCompatibilityMode.Full:
                case SchemaCompatibilityMode.FullAll:
                    foreach (var document in documents) {
                        var before = errors.Count;
                        CheckBackward(document.RootElement, unchecked_, errors);

                        // Full is backward AND forward, but the forward pass restates the same
                        // structural break in mirrored terms. Skip it when backward already failed
                        // so a single change is not reported twice.
                        if (errors.Count == before)
                            CheckForward(document.RootElement, unchecked_, errors);
                    }

                    break;

                default:
                    throw new ArgumentException("Invalid compatibility mode", nameof(compatibility));
            }

            return errors.Count > 0
                ? SchemaCompatibilityResult.Incompatible(errors)
                : SchemaCompatibilityResult.Compatible();
        }
        finally {
            foreach (var document in documents)
                document.Dispose();
        }
    }

    static void CheckBackward(JsonElement referenceRoot, JsonElement uncheckedRoot, List<SchemaCompatibilityError> errors) =>
        CheckBackwardProperties(referenceRoot, uncheckedRoot, referenceRoot, uncheckedRoot, errors, [], "#");

    static void CheckForward(JsonElement referenceRoot, JsonElement uncheckedRoot, List<SchemaCompatibilityError> errors) =>
        CheckForwardProperties(referenceRoot, uncheckedRoot, referenceRoot, uncheckedRoot, errors, [], "#");

    static void CheckBackwardProperties(
        JsonElement referenceSchema, JsonElement uncheckedSchema,
        JsonElement referenceRoot, JsonElement uncheckedRoot,
        List<SchemaCompatibilityError> errors, HashSet<(string, string)> visited, string path
    ) {
        if (!JsonSchemaStructure.Enter(ref referenceSchema, ref uncheckedSchema, referenceRoot, uncheckedRoot, visited))
            return;

        var referenceRequired = JsonSchemaStructure.Required(referenceSchema);
        var uncheckedRequired = JsonSchemaStructure.Required(uncheckedSchema);

        foreach (var (name, referenceProperty) in JsonSchemaStructure.Properties(referenceSchema)) {
            var propertyPath = $"{path}/{name}";

            if (!JsonSchemaStructure.TryGetSchemaProperty(uncheckedSchema, name, out var uncheckedProperty)) {
                if (referenceRequired.Contains(name))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.MissingRequiredProperty,
                            PropertyPath = propertyPath,
                            Details      = "Required property in original schema is missing in new schema"
                        }
                    );

                continue;
            }

            var resolvedReference = JsonSchemaStructure.Resolve(referenceProperty, referenceRoot);
            var resolvedUnchecked = JsonSchemaStructure.Resolve(uncheckedProperty, uncheckedRoot);

            CheckTypeCompatibility(resolvedReference, resolvedUnchecked, errors, propertyPath);

            if (!referenceRequired.Contains(name) && uncheckedRequired.Contains(name))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.OptionalToRequired,
                        PropertyPath = propertyPath,
                        Details      = "Property changed from optional to required, breaking backward compatibility"
                    }
                );

            CheckNested(
                referenceProperty, uncheckedProperty, referenceRoot, uncheckedRoot,
                errors, visited, propertyPath, backward: true
            );
        }

        foreach (var (name, _) in JsonSchemaStructure.Properties(uncheckedSchema)) {
            if (!JsonSchemaStructure.TryGetSchemaProperty(referenceSchema, name, out _) && uncheckedRequired.Contains(name))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.NewRequiredProperty,
                        PropertyPath = $"{path}/{name}",
                        Details      = "New required property breaks backward compatibility - old data won't have this field"
                    }
                );
        }
    }

    static void CheckForwardProperties(
        JsonElement referenceSchema, JsonElement uncheckedSchema,
        JsonElement referenceRoot, JsonElement uncheckedRoot,
        List<SchemaCompatibilityError> errors, HashSet<(string, string)> visited, string path
    ) {
        if (!JsonSchemaStructure.Enter(ref referenceSchema, ref uncheckedSchema, referenceRoot, uncheckedRoot, visited))
            return;

        var referenceRequired = JsonSchemaStructure.Required(referenceSchema);
        var uncheckedRequired = JsonSchemaStructure.Required(uncheckedSchema);

        foreach (var (name, uncheckedProperty) in JsonSchemaStructure.Properties(uncheckedSchema)) {
            var propertyPath = $"{path}/{name}";

            if (!JsonSchemaStructure.TryGetSchemaProperty(referenceSchema, name, out var referenceProperty)) {
                if (uncheckedRequired.Contains(name))
                    errors.Add(
                        new SchemaCompatibilityError {
                            Kind         = SchemaCompatibilityErrorKind.NewRequiredProperty,
                            PropertyPath = propertyPath,
                            Details      = "Required property in new schema is missing in original schema"
                        }
                    );

                continue;
            }

            var resolvedUnchecked = JsonSchemaStructure.Resolve(uncheckedProperty, uncheckedRoot);
            var resolvedReference = JsonSchemaStructure.Resolve(referenceProperty, referenceRoot);

            CheckTypeCompatibility(resolvedUnchecked, resolvedReference, errors, propertyPath);

            CheckNested(
                referenceProperty, uncheckedProperty, referenceRoot, uncheckedRoot,
                errors, visited, propertyPath, backward: false
            );
        }

        foreach (var (name, _) in JsonSchemaStructure.Properties(referenceSchema)) {
            if (!JsonSchemaStructure.TryGetSchemaProperty(uncheckedSchema, name, out _) && referenceRequired.Contains(name))
                errors.Add(
                    new SchemaCompatibilityError {
                        Kind         = SchemaCompatibilityErrorKind.RemovedProperty,
                        PropertyPath = $"{path}/{name}",
                        Details      = "Required property in original schema is missing in new schema"
                    }
                );
        }
    }

    static void CheckNested(
        JsonElement referenceProperty, JsonElement uncheckedProperty,
        JsonElement referenceRoot, JsonElement uncheckedRoot,
        List<SchemaCompatibilityError> errors, HashSet<(string, string)> visited, string propertyPath, bool backward
    ) {
        var resolvedReference = JsonSchemaStructure.Resolve(referenceProperty, referenceRoot);
        var resolvedUnchecked = JsonSchemaStructure.Resolve(uncheckedProperty, uncheckedRoot);

        var referenceType = JsonSchemaStructure.TypeOf(resolvedReference);
        var uncheckedType = JsonSchemaStructure.TypeOf(resolvedUnchecked);

        if (referenceType.HasFlag(SchemaValueType.Object) && uncheckedType.HasFlag(SchemaValueType.Object)) {
            Recurse(referenceProperty, uncheckedProperty, propertyPath);
            return;
        }

        if (!referenceType.HasFlag(SchemaValueType.Array) || !uncheckedType.HasFlag(SchemaValueType.Array))
            return;

        if (!JsonSchemaStructure.TryGetKeyword(resolvedReference, "items", out var referenceItems) ||
            !JsonSchemaStructure.TryGetKeyword(resolvedUnchecked, "items", out var uncheckedItems))
            return;

        var resolvedReferenceItems = JsonSchemaStructure.Resolve(referenceItems, referenceRoot);
        var resolvedUncheckedItems = JsonSchemaStructure.Resolve(uncheckedItems, uncheckedRoot);

        var referenceItemType = JsonSchemaStructure.TypeOf(resolvedReferenceItems);
        var uncheckedItemType = JsonSchemaStructure.TypeOf(resolvedUncheckedItems);

        if (referenceItemType != uncheckedItemType)
            errors.Add(
                new SchemaCompatibilityError {
                    Kind         = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
                    PropertyPath = propertyPath,
                    Details      = "Array items have incompatible type change",
                    OriginalType = backward ? referenceItemType : uncheckedItemType,
                    NewType      = backward ? uncheckedItemType : referenceItemType
                }
            );

        if (referenceItemType.HasFlag(SchemaValueType.Object) && uncheckedItemType.HasFlag(SchemaValueType.Object))
            Recurse(referenceItems, uncheckedItems, $"{propertyPath}/items");

        void Recurse(JsonElement reference, JsonElement unchecked_, string nestedPath) {
            if (backward)
                CheckBackwardProperties(reference, unchecked_, referenceRoot, uncheckedRoot, errors, visited, nestedPath);
            else
                CheckForwardProperties(reference, unchecked_, referenceRoot, uncheckedRoot, errors, visited, nestedPath);
        }
    }

    static void CheckTypeCompatibility(
        JsonElement source, JsonElement target, List<SchemaCompatibilityError> errors, string propertyPath
    ) {
        var sourceType = JsonSchemaStructure.TypeOf(source);
        var targetType = JsonSchemaStructure.TypeOf(target);

        if (sourceType == targetType)
            return;

        errors.Add(
            new SchemaCompatibilityError {
                Kind         = SchemaCompatibilityErrorKind.IncompatibleTypeChange,
                PropertyPath = propertyPath,
                Details      = "Property has incompatible type change",
                OriginalType = sourceType,
                NewType      = targetType
            }
        );
    }
}
