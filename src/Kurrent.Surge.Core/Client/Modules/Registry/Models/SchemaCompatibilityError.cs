using System.Collections;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Registry;

public readonly record struct SchemaCompatibilityError() {
    public SchemaCompatibilityErrorKind Kind    { get; init; } = SchemaCompatibilityErrorKind.Unspecified;
    public string                       Details { get; init; } = "";

    public string          PropertyPath { get; init; } = "";
    public SchemaValueType OriginalType { get; init; } = SchemaValueType.None;
    public SchemaValueType NewType      { get; init; } = SchemaValueType.None;

    public override string ToString() =>
        $"{Kind} at '{PropertyPath}': {Details}";
}

public record SchemaCompatibilityErrors : IEnumerable<SchemaCompatibilityError> {
    SchemaCompatibilityErrors(SchemaCompatibilityError[] errors) => Errors = errors;

    public IReadOnlyList<SchemaCompatibilityError> Errors { get; init; } = [];

    public static implicit operator SchemaCompatibilityErrors(SchemaCompatibilityError[] errors) =>
        new(errors);

    public static SchemaCompatibilityErrors From(params SchemaCompatibilityError[] errors) =>
        new(errors);

    public IEnumerator<SchemaCompatibilityError> GetEnumerator() => Errors.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public enum SchemaCompatibilityErrorKind {
    Unspecified,
    MissingRequiredProperty,  // Backward compatibility: Required property from old schema missing in new schema
    IncompatibleTypeChange,   // Backward compatibility: Property type changed incompatibly
    OptionalToRequired,       // Backward compatibility: Property changed from optional to required
    NewRequiredProperty,      // Forward compatibility: New required property added
    RemovedProperty,          // Forward compatibility: Property removed from schema
    ArrayTypeIncompatibility, // Issues with array item types
    DataFormatMismatch,       // Data format mismatch between schema versions
}
