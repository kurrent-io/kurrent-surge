using System.Collections.Concurrent;

namespace Kurrent.CodeAnalysis.Matching;

/// <summary>
/// Parsed form of a type-pattern string. Three forms supported:
/// <list type="bullet">
///   <item><c>"Ns.Foo"</c> — non-generic by full name</item>
///   <item><c>"Ns.Foo&lt;,&gt;"</c> — open generic by original definition + arity</item>
///   <item><c>"Ns.Foo&lt;string, int&gt;"</c> — closed generic, recursively matched</item>
/// </list>
/// Primitive aliases (<c>int</c>, <c>string</c>, etc.) are canonicalized. Short unqualified
/// names outside the alias table are rejected — fully qualified required otherwise.
/// </summary>
public readonly struct TypePattern {
    public string         Name       { get; }
    public bool           IsGeneric  { get; }
    public bool           IsOpen     { get; }
    public int            Arity      { get; }
    public TypePattern[]? ClosedArgs { get; }

    TypePattern(string name, bool isGeneric, bool isOpen, int arity, TypePattern[]? closedArgs) {
        Name       = name;
        IsGeneric  = isGeneric;
        IsOpen     = isOpen;
        Arity      = arity;
        ClosedArgs = closedArgs;
    }

    static readonly ConcurrentDictionary<string, TypePattern> Cache = new();

    public static TypePattern Parse(string pattern) =>
        Cache.GetOrAdd(pattern, ParseCore);

    static TypePattern ParseCore(string pattern) {
        pattern = pattern.Trim();
        if (pattern.Length == 0) throw new ArgumentException("Pattern cannot be empty", nameof(pattern));

        var ltIdx = pattern.IndexOf('<');
        if (ltIdx < 0) {
            var canonical = Canonicalize(pattern);
            return new TypePattern(canonical, false, false, 0, null);
        }

        if (!pattern.EndsWith(">", StringComparison.Ordinal))
            throw new ArgumentException($"Pattern '{pattern}' has unbalanced angle brackets", nameof(pattern));

        var name     = Canonicalize(pattern.Substring(0, ltIdx));
        var inner    = pattern.Substring(ltIdx + 1, pattern.Length - ltIdx - 2).Trim();
        var allComma = true;
        var commas   = 0;
        foreach (var ch in inner) {
            if (ch == ',') commas++;
            else if (!char.IsWhiteSpace(ch)) { allComma = false; break; }
        }

        if (allComma)
            return new TypePattern(name, true, true, commas + 1, null);

        var parts = SplitTopLevelCommas(inner);
        var args  = new TypePattern[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            args[i] = ParseCore(parts[i]);

        return new TypePattern(name, true, false, args.Length, args);
    }

    static List<string> SplitTopLevelCommas(string s) {
        var result = new List<string>();
        var depth  = 0;
        var start  = 0;
        for (var i = 0; i < s.Length; i++) {
            var ch = s[i];
            if (ch == '<') depth++;
            else if (ch == '>') depth--;
            else if (ch == ',' && depth == 0) {
                result.Add(s.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        result.Add(s.Substring(start).Trim());
        return result;
    }

    static readonly Dictionary<string, string> PrimitiveAliases = new(StringComparer.Ordinal) {
        { "int",     "System.Int32"   },
        { "uint",    "System.UInt32"  },
        { "long",    "System.Int64"   },
        { "ulong",   "System.UInt64"  },
        { "short",   "System.Int16"   },
        { "ushort",  "System.UInt16"  },
        { "byte",    "System.Byte"    },
        { "sbyte",   "System.SByte"   },
        { "float",   "System.Single"  },
        { "double",  "System.Double"  },
        { "decimal", "System.Decimal" },
        { "bool",    "System.Boolean" },
        { "char",    "System.Char"    },
        { "string",  "System.String"  },
        { "object",  "System.Object"  },
        { "nint",    "System.IntPtr"  },
        { "nuint",   "System.UIntPtr" }
    };

    static string Canonicalize(string name) {
        name = name.Trim();
        if (PrimitiveAliases.TryGetValue(name, out var full)) return full;
        if (name.IndexOf('.') < 0 && !PrimitiveAliases.ContainsKey(name))
            throw new ArgumentException(
                $"Unqualified name '{name}' is not a recognized primitive alias. Use the fully qualified form.",
                nameof(name));
        return name;
    }

    public bool Matches(TypeSymbol type) {
        var typeName = StripGenericSuffix(type.FullName);
        if (!string.Equals(typeName, Name, StringComparison.Ordinal)) return false;

        if (!IsGeneric) return type.TypeArguments.Length == 0;

        if (type.TypeArguments.Length != Arity) return false;

        if (IsOpen) return true;

        if (ClosedArgs is null) return true;

        for (var i = 0; i < Arity; i++)
            if (!ClosedArgs[i].Matches(type.TypeArguments[i])) return false;

        return true;
    }

    static string StripGenericSuffix(string fullName) {
        var lt = fullName.IndexOf('<');
        return lt < 0 ? fullName : fullName.Substring(0, lt);
    }
}
