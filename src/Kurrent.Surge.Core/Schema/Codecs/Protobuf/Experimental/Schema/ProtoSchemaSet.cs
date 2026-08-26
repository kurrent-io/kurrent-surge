using System.Collections.Frozen;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// One <c>.proto</c> source, named the way its importers spell it.
/// </summary>
/// <param name="Name">The import path, such as <c>orders/v1/order.proto</c>.</param>
/// <param name="Content">The schema text.</param>
[PublicAPI]
public readonly record struct ProtoFile(string Name, string Content);

/// <summary>
/// A resolved set of <c>.proto</c> schemas, built from text.
/// </summary>
/// <remarks>
/// Nothing is read from disk. A file name is an identity string used to match imports within the
/// set, so a schema pulled from a registry needs no filesystem presence.
/// <para>
/// Imports of <c>google/protobuf/*.proto</c> resolve to the descriptors compiled into
/// Google.Protobuf rather than to parsed text, which keeps the well-known types byte-identical to
/// what every other implementation sees.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ProtoSchemaSet {
    ProtoSchemaSet(IReadOnlyList<FileDescriptor> files, FrozenDictionary<string, MessageDescriptor> messages) {
        Files     = files;
        _messages = messages;
    }

    readonly FrozenDictionary<string, MessageDescriptor> _messages;

    /// <summary>The descriptors built from the set, dependencies first.</summary>
    public IReadOnlyList<FileDescriptor> Files { get; }

    /// <summary>Every message in the set, keyed by fully qualified name.</summary>
    public IReadOnlyCollection<string> MessageNames => _messages.Keys;

    /// <summary>Builds a schema set from <c>.proto</c> text.</summary>
    /// <exception cref="ProtoParseException">A schema could not be parsed.</exception>
    /// <exception cref="ArgumentException">An import cannot be resolved, or the set is not a valid schema.</exception>
    public static ProtoSchemaSet Build(params ProtoFile[] files) {
        if (files.Length == 0) throw new ArgumentException("At least one schema is required.", nameof(files));

        var sources = files.ToDictionary(file => file.Name, file => file.Content, StringComparer.Ordinal);
        var ordered = new List<FileDescriptorProto>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files) Visit(file.Name, sources, visited, ordered);

        ResolveEnumFieldTypes(ordered);

        IReadOnlyList<FileDescriptor> built;

        try {
            built = FileDescriptor.BuildFromByteStrings(ordered.Select(file => file.ToByteString()));
        }
        catch (Exception ex) when (ex is not ProtoParseException) {
            throw new ArgumentException($"The schema set could not be resolved: {ex.Message}", nameof(files), ex);
        }

        var messages = new Dictionary<string, MessageDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in built)
            foreach (var message in descriptor.MessageTypes)
                Collect(message, messages);

        return new ProtoSchemaSet(built, messages.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>Builds a schema set from a single <c>.proto</c> source that imports nothing outside the well-known types.</summary>
    /// <exception cref="ProtoParseException">The schema could not be parsed.</exception>
    /// <exception cref="ArgumentException">The schema is not valid.</exception>
    public static ProtoSchemaSet Build(string schema, string name = "schema.proto") =>
        Build(new ProtoFile(name, schema));

    /// <summary>Finds a message by fully qualified name, such as <c>orders.v1.Order</c>.</summary>
    /// <exception cref="ArgumentException">No such message is declared in the set.</exception>
    public MessageDescriptor FindMessage(string fullName) =>
        TryFindMessage(fullName)
     ?? throw new ArgumentException($"The message '{fullName}' is not declared in this schema set.", nameof(fullName));

    /// <summary>Finds a message by fully qualified name, or returns null.</summary>
    public MessageDescriptor? TryFindMessage(string fullName) => _messages.GetValueOrDefault(fullName);

    /// <remarks>
    /// Post-order: a file is appended only once its imports are, because
    /// <c>BuildFromByteStrings</c> resolves each dependency against what precedes it and fails
    /// outright on a forward reference.
    /// </remarks>
    static void Visit(string name, Dictionary<string, string> sources, HashSet<string> visited, List<FileDescriptorProto> ordered) {
        if (!visited.Add(name)) return;

        // A well-known type arrives already compiled. Converting it here rather than at the end
        // means one form reaches every later stage, so nothing downstream has to ask which it is.
        if (WellKnown.TryGetValue(name, out var wellKnown)) {
            foreach (var dependency in wellKnown.Dependencies) Visit(dependency.Name, sources, visited, ordered);

            ordered.Add(wellKnown.ToProto());

            return;
        }

        if (!sources.TryGetValue(name, out var content))
            throw new ArgumentException($"The import '{name}' is not present in the schema set and is not a well-known type.", nameof(name));

        var file = ProtoParser.Parse(name, content);

        foreach (var dependency in file.Dependency) Visit(dependency, sources, visited, ordered);

        ordered.Add(file);
    }

    #region ->> Enum resolution <<-

    /// <summary>
    /// Retypes every field that names an enum, which the parser could only guess was a message.
    /// </summary>
    /// <remarks>
    /// A single pass over <c>.proto</c> text cannot tell <c>Priority p = 1;</c> from a message-typed
    /// field: the name may be declared later, or in a file not yet read. The descriptor builder
    /// validates rather than infers — it reports <c>"Priority" is not a message type</c> — so the
    /// distinction has to be settled here, once every file in the set has been parsed.
    /// </remarks>
    static void ResolveEnumFieldTypes(List<FileDescriptorProto> files) {
        var enums = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files) CollectEnums(file, enums);

        if (enums.Count == 0) return;

        // Well-known files are walked too. Their enum-typed fields already carry the right type, so
        // the pass finds nothing to do there — which is cheaper than the special case that skipped it.
        foreach (var file in files) {
            foreach (var field in file.Extension) Retype(field, file.Package, enums);

            foreach (var message in file.MessageType) RetypeMessage(message, file.Package, enums);
        }
    }

    static void RetypeMessage(DescriptorProto message, string scope, HashSet<string> enums) {
        var inner = scope.Length == 0 ? message.Name : $"{scope}.{message.Name}";

        foreach (var field in message.Field) Retype(field, inner, enums);

        foreach (var field in message.Extension) Retype(field, inner, enums);

        foreach (var nested in message.NestedType) RetypeMessage(nested, inner, enums);
    }

    static void Retype(FieldDescriptorProto field, string scope, HashSet<string> enums) {
        if (field.Type != FieldDescriptorProto.Types.Type.Message || field.TypeName.Length == 0) return;

        if (Resolve(field.TypeName, scope, enums) is not { } resolved) return;

        field.Type     = FieldDescriptorProto.Types.Type.Enum;
        field.TypeName = "." + resolved;
    }

    /// <remarks>
    /// Mirrors how the language resolves a relative name: innermost enclosing scope first, then
    /// outward one segment at a time, then the global scope. A leading dot is already absolute.
    /// See https://protobuf.dev/programming-guides/proto3/#packages.
    /// </remarks>
    static string? Resolve(string typeName, string scope, HashSet<string> enums) {
        if (typeName.StartsWith('.')) return enums.Contains(typeName[1..]) ? typeName[1..] : null;

        while (scope.Length > 0) {
            var candidate = $"{scope}.{typeName}";

            if (enums.Contains(candidate)) return candidate;

            var separator = scope.LastIndexOf('.');

            scope = separator < 0 ? "" : scope[..separator];
        }

        return enums.Contains(typeName) ? typeName : null;
    }

    static void CollectEnums(FileDescriptorProto file, HashSet<string> enums) {
        var scope = file.Package;

        foreach (var declared in file.EnumType) enums.Add(scope.Length == 0 ? declared.Name : $"{scope}.{declared.Name}");

        foreach (var message in file.MessageType) CollectEnums(message, scope, enums);
    }

    static void CollectEnums(DescriptorProto message, string scope, HashSet<string> enums) {
        var inner = scope.Length == 0 ? message.Name : $"{scope}.{message.Name}";

        foreach (var declared in message.EnumType) enums.Add($"{inner}.{declared.Name}");

        foreach (var nested in message.NestedType) CollectEnums(nested, inner, enums);
    }

    #endregion // Enum resolution

    static void Collect(MessageDescriptor message, Dictionary<string, MessageDescriptor> messages) {
        messages[message.FullName] = message;

        foreach (var nested in message.NestedTypes) Collect(nested, messages);
    }

    /// <remarks>
    /// Taken from the descriptors Google.Protobuf already carries rather than from schema text.
    /// A hand-maintained copy of these files is a second source of truth that drifts, and the
    /// compiled ones are what <c>protoc</c> produced.
    /// </remarks>
    static readonly FrozenDictionary<string, FileDescriptor> WellKnown = new[] {
        Timestamp.Descriptor.File,
        Duration.Descriptor.File,
        Struct.Descriptor.File,
        Any.Descriptor.File,
        Empty.Descriptor.File,
        FieldMask.Descriptor.File,
        DoubleValue.Descriptor.File,
        SourceContext.Descriptor.File,
        Google.Protobuf.WellKnownTypes.Type.Descriptor.File,
        Api.Descriptor.File,
        DescriptorProto.Descriptor.File
    }.ToFrozenDictionary(file => file.Name, StringComparer.Ordinal);
}
