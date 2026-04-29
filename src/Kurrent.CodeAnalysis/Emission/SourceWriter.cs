using System.Text;

namespace Kurrent.CodeAnalysis.Emission;

public static class SourceWriter {
    /// <summary>
    /// Wraps <paramref name="innerSource"/> in nested <c>partial</c> declarations for every
    /// <paramref name="containingTypes"/> entry, optionally inside a namespace declaration.
    /// Each containing type's modifiers/kind are reproduced from <see cref="ContainingTypeRef"/>.
    /// </summary>
    public static string WrapInContainers(string @namespace, IReadOnlyList<ContainingTypeRef> containingTypes, string innerSource) {
        var sb    = new StringBuilder();
        var depth = 0;

        if (!string.IsNullOrEmpty(@namespace)) {
            sb.Append("namespace ").Append(@namespace).AppendLine(";");
            sb.AppendLine();
        }

        foreach (var c in containingTypes) {
            AppendIndent(sb, depth);
            sb.Append(HeaderFor(c)).AppendLine();
            AppendIndent(sb, depth);
            sb.AppendLine("{");
            depth++;
        }

        foreach (var line in innerSource.Split(new[] { '\n' }, StringSplitOptions.None)) {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) { sb.AppendLine(); continue; }
            AppendIndent(sb, depth);
            sb.AppendLine(trimmed);
        }

        for (var i = containingTypes.Count - 1; i >= 0; i--) {
            depth--;
            AppendIndent(sb, depth);
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    static string HeaderFor(ContainingTypeRef c) {
        var sb = new StringBuilder();

        if (c.Traits.HasFlag(TypeTraits.Public))         sb.Append("public ");
        else if (c.Traits.HasFlag(TypeTraits.Internal))  sb.Append("internal ");
        else if (c.Traits.HasFlag(TypeTraits.Protected)) sb.Append("protected ");
        else if (c.Traits.HasFlag(TypeTraits.Private))   sb.Append("private ");

        if (c.Traits.HasFlag(TypeTraits.Static))   sb.Append("static ");
        if (c.Traits.HasFlag(TypeTraits.Abstract)) sb.Append("abstract ");
        if (c.Traits.HasFlag(TypeTraits.Sealed))   sb.Append("sealed ");
        if (c.Traits.HasFlag(TypeTraits.ReadOnly)) sb.Append("readonly ");

        sb.Append("partial ");

        sb.Append(c.Kind switch {
            TypeKind.Class        => "class ",
            TypeKind.Record       => "record ",
            TypeKind.Struct       => "struct ",
            TypeKind.RecordStruct => "record struct ",
            TypeKind.Interface    => "interface ",
            _                     => "class "
        });

        sb.Append(c.Name);

        if (c.TypeParameterNames.Length > 0) {
            sb.Append('<');
            for (var i = 0; i < c.TypeParameterNames.Length; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append(c.TypeParameterNames[i]);
            }
            sb.Append('>');
        }

        return sb.ToString();
    }

    static void AppendIndent(StringBuilder sb, int depth) {
        for (var i = 0; i < depth; i++) sb.Append("    ");
    }
}
